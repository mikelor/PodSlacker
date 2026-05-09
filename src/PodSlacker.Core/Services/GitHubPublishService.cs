using System.Text;
using Microsoft.Extensions.Logging;
using Octokit;

namespace PodSlacker.Core.Services;

/// <summary>
/// Publishes a generated HTML page (and optional companion assets) to GitHub Pages
/// using the Octokit SDK's Git Data API.
///
/// The Git Data API (Blob → Tree → Commit → Reference) is used instead of the
/// higher-level Contents API because the Contents API's <c>CreateFileRequest.Content</c>
/// property is automatically base64-encoded by Octokit's JSON serialiser before the
/// request is sent. Passing pre-encoded base64 into that property results in the
/// content being double-encoded: GitHub decodes it once and stores our first base64
/// string as the literal file content — the classic "binary blob" symptom.
///
/// By using <see cref="NewBlob"/> with <see cref="EncodingType.Base64"/> we tell
/// GitHub exactly how to interpret the content, and binary assets (audio, images)
/// are stored correctly without any encoding ambiguity.
/// </summary>
public sealed class GitHubPublishService(ILogger<GitHubPublishService> logger)
{
    /// <summary>
    /// Publishes a generated HTML page to GitHub Pages.
    /// Creates the repository and the publish branch if they do not already exist,
    /// then creates or updates files with a single commit.
    /// After committing, scans the full branch tree and regenerates a README.md
    /// index listing all published pages organised by folder.
    /// </summary>
    /// <param name="pagePath">Absolute path to the local HTML file to publish.</param>
    /// <param name="repoName">
    /// GitHub repository name (not the full <c>owner/repo</c> slug — the authenticated
    /// user's login is used as the owner).
    /// </param>
    /// <param name="tokenEnvVar">
    /// Name of the environment variable that holds a GitHub Personal Access Token
    /// with <c>repo</c> and <c>pages</c> scopes.
    /// </param>
    /// <param name="branch">Git branch to use as the GitHub Pages source.</param>
    /// <param name="tokenValue">
    /// Literal Personal Access Token supplied by the caller (e.g. from the web UI form).
    /// When non-empty this takes precedence over <paramref name="tokenEnvVar"/>.
    /// </param>
    /// <param name="assetPaths">
    /// Optional list of additional files (audio, images, transcript, etc.) to publish
    /// alongside the HTML. Each file is committed using its filename, under
    /// <paramref name="folder"/> when non-empty.
    /// </param>
    /// <param name="folder">
    /// Optional subfolder within the gh-pages branch where all files are placed
    /// (e.g. <c>"google-cloud-next"</c>). Leave empty to publish to the branch root.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public GitHub Pages URL where the page will be accessible.</returns>
    public async Task<string> PublishAsync(
        string  pagePath,
        string  repoName       = "podslacker-pages",
        string  tokenEnvVar    = "GITHUB_TOKEN",
        string  branch         = "gh-pages",
        string? tokenValue     = null,
        IReadOnlyList<string>? assetPaths = null,
        string  folder         = "",
        CancellationToken ct   = default)
    {
        // Normalise folder: strip leading/trailing slashes, treat whitespace-only as empty.
        folder = folder.Trim().Trim('/');

        // Prefer a literal token passed by the caller (e.g. from the web UI form)
        // over the server-side environment variable.
        string? token = tokenValue is { Length: > 0 }
            ? tokenValue
            : Environment.GetEnvironmentVariable(tokenEnvVar);

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                $"GitHub token not found. Either supply a Personal Access Token in the " +
                $"publish form or set '{tokenEnvVar}' on the server. " +
                "Create a token at: https://github.com/settings/tokens");

        var github = new GitHubClient(new ProductHeaderValue("PodSlacker"));
        github.Credentials = new Credentials(token);

        // 1. Authenticate
        var me = await github.User.Current();
        logger.LogInformation("Authenticated as: {User}", me.Login);
        string username = me.Login;

        // 2. Create repo if needed
        Repository? repo = null;
        try
        {
            repo = await github.Repository.Get(username, repoName);
            logger.LogInformation("Repository: github.com/{User}/{Repo}", username, repoName);
        }
        catch (NotFoundException)
        {
            logger.LogInformation("Creating repository '{Repo}'…", repoName);
            repo = await github.Repository.Create(new NewRepository(repoName)
            {
                Description = "PodSlacker generated podcast pages",
                Private     = false,
                AutoInit    = true,
            });
            logger.LogInformation("Repository created: github.com/{User}/{Repo}", username, repoName);
            await Task.Delay(2000, ct);   // let GitHub finish initialising
        }

        string defaultBranch = repo.DefaultBranch;

        // 3. Ensure publish branch exists
        string branchHeadSha;
        try
        {
            var branchRef = await github.Git.Reference.Get(username, repoName, $"heads/{branch}");
            branchHeadSha = branchRef.Object.Sha;
            logger.LogInformation("Branch '{Branch}' exists at {Sha}", branch, branchHeadSha[..8]);
        }
        catch (NotFoundException)
        {
            var defaultRef = await github.Git.Reference.Get(username, repoName, $"heads/{defaultBranch}");
            branchHeadSha  = defaultRef.Object.Sha;
            await github.Git.Reference.Create(username, repoName,
                new NewReference($"refs/heads/{branch}", branchHeadSha));
            logger.LogInformation("Created branch '{Branch}'", branch);
        }

        // Helper: prefix a filename with the folder path when folder is non-empty.
        string Prefix(string filename) => string.IsNullOrEmpty(folder)
            ? filename
            : $"{folder}/{filename}";

        // 4. Build the list of files to commit.
        //    All files are placed under <folder>/ when folder is non-empty.
        var filesToCommit = new List<(string localPath, string remoteName)>
        {
            (pagePath, Prefix(Path.GetFileName(pagePath)))
        };

        if (assetPaths is { Count: > 0 })
        {
            foreach (string assetPath in assetPaths)
            {
                if (!File.Exists(assetPath))
                {
                    logger.LogWarning("Asset file not found, skipping: {Path}", assetPath);
                    continue;
                }
                filesToCommit.Add((assetPath, Prefix(Path.GetFileName(assetPath))));
            }
        }

        // 5. Upload all files as Git blobs and build a tree.
        //
        // We use the Git Data API (Blob → Tree → Commit → Ref) rather than the
        // Contents API because Contents.CreateFile / UpdateFile auto-encodes the
        // Content string inside Octokit, which would double-encode any base64 we
        // already computed — causing GitHub to store our base64 string as the
        // literal file content instead of the decoded bytes.
        //
        // NewBlob with EncodingType.Base64 sends the content to GitHub exactly as
        // provided and tells GitHub to decode it once when storing.

        var treeItems = new List<NewTreeItem>();
        foreach (var (localPath, remoteName) in filesToCommit)
        {
            byte[] bytes = await File.ReadAllBytesAsync(localPath, ct);
            string b64   = Convert.ToBase64String(bytes);

            logger.LogInformation("Uploading blob: {File} ({Kb} KB)…",
                remoteName, bytes.Length / 1024);

            var blob = await github.Git.Blob.Create(username, repoName, new NewBlob
            {
                Content  = b64,
                Encoding = EncodingType.Base64,
            });

            treeItems.Add(new NewTreeItem
            {
                Path = remoteName,
                Mode = "100644",        // regular file
                Type = TreeType.Blob,
                Sha  = blob.Sha,
            });

            logger.LogInformation("Blob created: {File} → {Sha}", remoteName, blob.Sha[..8]);
        }

        // 6. Get the current commit's tree so unchanged files are preserved.
        var headCommit = await github.Git.Commit.Get(username, repoName, branchHeadSha);

        // 7. Scan the existing base tree for all _page.html and _transcript.txt files.
        //    We merge these with the new files from this commit to generate a complete
        //    README index that reflects everything that will be in the branch after commit.
        var baseTree = await github.Git.Tree.GetRecursive(username, repoName, headCommit.Tree.Sha);

        var newRemotePaths = treeItems.Select(t => t.Path).ToHashSet(StringComparer.Ordinal);

        var allPagePaths = baseTree.Tree
            .Where(t => t.Type == TreeType.Blob && t.Path.EndsWith("_page.html")
                        && !newRemotePaths.Contains(t.Path))  // will be replaced by new commit
            .Select(t => t.Path)
            .Concat(treeItems.Where(t => t.Path.EndsWith("_page.html")).Select(t => t.Path))
            .OrderBy(p => p)
            .ToList();

        var allTranscriptPaths = baseTree.Tree
            .Where(t => t.Type == TreeType.Blob && t.Path.EndsWith("_transcript.txt")
                        && !newRemotePaths.Contains(t.Path))
            .Select(t => t.Path)
            .Concat(treeItems.Where(t => t.Path.EndsWith("_transcript.txt")).Select(t => t.Path))
            .ToHashSet(StringComparer.Ordinal);

        // 8. Generate README.md and upload it as a blob.
        if (allPagePaths.Count > 0)
        {
            string readmeContent = GenerateReadme(username, repoName, allPagePaths, allTranscriptPaths);
            byte[] readmeBytes   = Encoding.UTF8.GetBytes(readmeContent);
            string readmeB64     = Convert.ToBase64String(readmeBytes);

            var readmeBlob = await github.Git.Blob.Create(username, repoName, new NewBlob
            {
                Content  = readmeB64,
                Encoding = EncodingType.Base64,
            });

            treeItems.Add(new NewTreeItem
            {
                Path = "README.md",
                Mode = "100644",
                Type = TreeType.Blob,
                Sha  = readmeBlob.Sha,
            });

            logger.LogInformation("README.md regenerated with {Count} page(s)", allPagePaths.Count);
        }

        // 9. Create the new tree (base + overrides).
        // NewTree.Tree has a private setter (Collection<NewTreeItem>); add items one by one.
        var newTreeDef = new NewTree { BaseTree = headCommit.Tree.Sha };
        foreach (var item in treeItems)
            newTreeDef.Tree.Add(item);

        var newTree = await github.Git.Tree.Create(username, repoName, newTreeDef);
        logger.LogInformation("Tree created: {Sha}", newTree.Sha[..8]);

        // 10. Create a commit pointing to the new tree.
        string pageFilename = Path.GetFileName(pagePath);
        string commitMsg    = filesToCommit.Count == 1
            ? $"Update {pageFilename} via PodSlacker"
            : $"Update {pageFilename} + {filesToCommit.Count - 1} asset(s) via PodSlacker";

        // NewCommit takes parents as an IEnumerable<string>.
        var newCommit = await github.Git.Commit.Create(username, repoName,
            new NewCommit(commitMsg, newTree.Sha, new[] { branchHeadSha }));
        logger.LogInformation("Commit created: {Sha} — {Msg}", newCommit.Sha[..8], commitMsg);

        // 11. Advance the branch reference to the new commit.
        await github.Git.Reference.Update(username, repoName, $"heads/{branch}",
            new ReferenceUpdate(newCommit.Sha));
        logger.LogInformation("Branch '{Branch}' updated to {Sha}", branch, newCommit.Sha[..8]);

        // 12. Check GitHub Pages status and advise if not yet enabled.
        // Note: Octokit 14 removed the Pages Update API; the user needs to enable
        // Pages manually once in Settings → Pages → Source = branch 'gh-pages'.
        try
        {
            await github.Repository.Page.Get(username, repoName);
            logger.LogInformation("GitHub Pages already enabled");
        }
        catch (NotFoundException)
        {
            logger.LogWarning(
                "GitHub Pages is not yet enabled for github.com/{User}/{Repo}. " +
                "Visit https://github.com/{User}/{Repo}/settings/pages, " +
                "set the source to branch '{Branch}', and your page will be live.",
                username, repoName, username, repoName, branch);
        }

        string remotePage = Prefix(pageFilename);
        return $"https://{username}.github.io/{repoName}/{remotePage}";
    }

    // ── README generator ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates a Markdown README that lists every published page organised by folder,
    /// with links to the GitHub Pages URL and, where available, the transcript download.
    /// </summary>
    private static string GenerateReadme(
        string                      username,
        string                      repoName,
        IEnumerable<string>         pagePaths,
        IReadOnlyCollection<string> transcriptPaths)
    {
        string ghPagesBase = $"https://{username}.github.io/{repoName}";

        // Group by parent folder; empty string means the branch root.
        var byFolder = pagePaths
            .GroupBy(p =>
            {
                int slash = p.LastIndexOf('/');
                return slash >= 0 ? p[..slash] : "";
            })
            // Root first, then alphabetical by folder name.
            .OrderBy(g => string.IsNullOrEmpty(g.Key) ? "\0" : g.Key)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# PodSlacker — Published Pages");
        sb.AppendLine();
        sb.AppendLine("*Auto-generated index of all podcast pages published to this repository.*");
        sb.AppendLine();

        foreach (var group in byFolder)
        {
            string heading = string.IsNullOrEmpty(group.Key) ? "Root" : group.Key;
            sb.AppendLine($"## {heading}");
            sb.AppendLine();
            sb.AppendLine("| Video | Transcript |");
            sb.AppendLine("|-------|------------|");

            foreach (string pagePath in group.OrderBy(p => p))
            {
                string pageFile = Path.GetFileName(pagePath);

                // Derive a readable title from the filename pattern
                // "{sanitized_title}_{11-char-videoId}_page.html"
                string titleSlug = pageFile.EndsWith("_page.html", StringComparison.Ordinal)
                    ? pageFile[..^"_page.html".Length]
                    : pageFile;

                // Strip trailing _XXXXXXXXXXX (underscore + 11-char YouTube video ID).
                if (titleSlug.Length > 12 && titleSlug[^12] == '_')
                    titleSlug = titleSlug[..^12];

                // Convert underscores to spaces and apply simple title-case.
                string displayTitle = string.Join(" ",
                    titleSlug.Split('_', StringSplitOptions.RemoveEmptyEntries)
                             .Select(w => w.Length > 0
                                 ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()
                                 : w));

                string pageUrl = $"{ghPagesBase}/{pagePath}";

                // Check whether a companion transcript exists.
                string transcriptFile = pageFile.Replace("_page.html", "_transcript.txt",
                    StringComparison.Ordinal);
                string transcriptRemotePath = string.IsNullOrEmpty(group.Key)
                    ? transcriptFile
                    : $"{group.Key}/{transcriptFile}";

                string transcriptCol = transcriptPaths.Contains(transcriptRemotePath)
                    ? $"[📄 Download]({ghPagesBase}/{transcriptRemotePath})"
                    : "—";

                sb.AppendLine($"| [{displayTitle}]({pageUrl}) | {transcriptCol} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
