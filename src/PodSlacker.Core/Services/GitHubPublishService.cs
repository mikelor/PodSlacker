using Microsoft.Extensions.Logging;
using Octokit;

namespace PodSlacker.Core.Services;

/// <summary>
/// Publishes a generated HTML page to GitHub Pages using the Octokit SDK.
/// Mirrors the Python publish_to_github() function step-for-step.
/// </summary>
public sealed class GitHubPublishService(ILogger<GitHubPublishService> logger)
{
    /// <summary>
    /// Publishes a generated HTML page to GitHub Pages.
    /// Creates the repository and the publish branch if they do not already exist,
    /// then creates or updates the file with a commit.
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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public GitHub Pages URL where the page will be accessible.</returns>
    public async Task<string> PublishAsync(
        string  pagePath,
        string  repoName       = "podslacker-pages",
        string  tokenEnvVar    = "GITHUB_TOKEN",
        string  branch         = "gh-pages",
        string? tokenValue     = null,
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

        var github   = new GitHubClient(new ProductHeaderValue("PodSlacker"));
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
        try
        {
            await github.Repository.Branch.Get(username, repoName, branch);
            logger.LogInformation("Branch: {Branch}", branch);
        }
        catch (NotFoundException)
        {
            var defaultRef = await github.Git.Reference.Get(username, repoName, $"heads/{defaultBranch}");
            string sha     = defaultRef.Object.Sha;
            await github.Git.Reference.Create(username, repoName,
                new NewReference($"refs/heads/{branch}", sha));
            logger.LogInformation("Created branch: {Branch}", branch);
        }

        // 4. Create or update the file
        string filename   = Path.GetFileName(pagePath);
        byte[] pageBytes  = await File.ReadAllBytesAsync(pagePath, ct);
        string b64Content = Convert.ToBase64String(pageBytes);

        RepositoryContent? existing = null;
        try
        {
            var contents = await github.Repository.Content.GetAllContentsByRef(
                username, repoName, filename, branch);
            existing = contents.FirstOrDefault();
        }
        catch (NotFoundException) { /* file doesn't exist yet — that's fine */ }

        // Octokit 14.0.0 removed the ContentBytes property setter — content is
        // passed as base64 in the constructor, which is what b64Content already is.
        string commitMsg = $"Update {filename} via podslacker";
        if (existing is not null)
        {
            logger.LogInformation("Updating {File}…", filename);
            await github.Repository.Content.UpdateFile(
                username, repoName, filename,
                new UpdateFileRequest(commitMsg, b64Content, existing.Sha, branch));
        }
        else
        {
            logger.LogInformation("Uploading {File}…", filename);
            await github.Repository.Content.CreateFile(
                username, repoName, filename,
                new CreateFileRequest(commitMsg, b64Content, branch));
        }
        logger.LogInformation("File published to branch '{Branch}'", branch);

        // 5. Check GitHub Pages status and advise if not yet enabled.
        // Note: Octokit 14 removed the Pages Update/BranchSource API that previously
        // allowed programmatic enabling. The file is uploaded successfully above —
        // if Pages isn't enabled yet the user just needs to do it once in Settings.
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

        return $"https://{username}.github.io/{repoName}/{filename}";
    }
}
