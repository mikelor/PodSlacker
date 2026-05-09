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
    /// Optional list of additional files (audio, images, etc.) to publish alongside the HTML.
    /// Each file is committed to the root of the branch using its filename.
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
        CancellationToken ct   = default)
    {
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

        // 4. Upload all files as Git blobs and build a tree.
        //
        // We use the Git Data API (Blob → Tree → Commit → Ref) rather than the
        // Contents API because Contents.CreateFile / UpdateFile auto-encodes the
        // Content string inside Octokit, which would double-encode any base64 we
        // already computed — causing GitHub to store our base64 string as the
        // literal file content instead of the decoded bytes.
        //
        // NewBlob with EncodingType.Base64 sends the content to GitHub exactly as
        // provided and tells GitHub to decode it once when storing.

        var filesToCommit = new List<(string localPath, string remoteName)>
        {
            (pagePath, Path.GetFileName(pagePath))
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
                filesToCommit.Add((assetPath, Path.GetFileName(assetPath)));
            }
        }

        // Upload each file as a blob and collect tree items.
        var treeItems = new List<NewTreeItem>();
        foreach (var (localPath, remoteName) in filesToCommit)
        {
            byte[] bytes   = await File.ReadAllBytesAsync(localPath, ct);
            string b64     = Convert.ToBase64String(bytes);

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

        // 5. Get the current commit's tree so unchanged files are preserved.
        var headCommit = await github.Git.Commit.Get(username, repoName, branchHeadSha);

        // NewTree.Tree has a private setter (Collection<NewTreeItem>); add items one by one.
        var newTreeDef = new NewTree { BaseTree = headCommit.Tree.Sha };
        foreach (var item in treeItems)
            newTreeDef.Tree.Add(item);

        var newTree = await github.Git.Tree.Create(username, repoName, newTreeDef);
        logger.LogInformation("Tree created: {Sha}", newTree.Sha[..8]);

        // 6. Create a commit pointing to the new tree.
        string pageFilename = Path.GetFileName(pagePath);
        string commitMsg    = filesToCommit.Count == 1
            ? $"Update {pageFilename} via PodSlacker"
            : $"Update {pageFilename} + {filesToCommit.Count - 1} asset(s) via PodSlacker";

        // NewCommit takes parents as an IEnumerable<string>.
        var newCommit = await github.Git.Commit.Create(username, repoName,
            new NewCommit(commitMsg, newTree.Sha, new[] { branchHeadSha }));
        logger.LogInformation("Commit created: {Sha} — {Msg}", newCommit.Sha[..8], commitMsg);

        // 7. Advance the branch reference to the new commit.
        await github.Git.Reference.Update(username, repoName, $"heads/{branch}",
            new ReferenceUpdate(newCommit.Sha));
        logger.LogInformation("Branch '{Branch}' updated to {Sha}", branch, newCommit.Sha[..8]);

        // 8. Check GitHub Pages status and advise if not yet enabled.
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

        return $"https://{username}.github.io/{repoName}/{pageFilename}";
    }
}
