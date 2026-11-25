<#
.SYNOPSIS
    Creates, configures and deploys PodSlacker on Fly.io.

.DESCRIPTION
    Three independent steps, chosen with switches. Each one is safe to re-run.

      -Setup    Create the two Fly apps and their volumes if they do not exist.
      -Secrets  Push secrets from the repo's .env file to Fly.
      -Deploy   Build and deploy (Fly's remote builders; no local Docker needed).

    First time:   ./scripts/fly-deploy.ps1 -Setup -Secrets -Deploy
    Redeploying:  ./scripts/fly-deploy.ps1 -Deploy
    Rotating keys: edit .env, then ./scripts/fly-deploy.ps1 -Secrets

    Secret values are piped to `fly secrets import` over stdin. They are never
    printed and never appear on a command line.

.PARAMETER App
    Which app to act on: api, web, or all (default).

.PARAMETER Region
    Fly region for new volumes. Must match primary_region in the fly.*.toml files.

.PARAMETER EnvFile
    Settings file to read secrets from. Defaults to .env in the repo root.

.PARAMETER IncludeGitHubToken
    Also push GITHUB_TOKEN to the API. Off by default: the web UI is public and
    has no login, and with a server-side token any visitor can tick "Publish to
    GitHub Pages" and publish under your account.

.PARAMETER NoCache
    Force a clean image build (no Docker layer cache).

.EXAMPLE
    ./scripts/fly-deploy.ps1 -Setup -Secrets -Deploy

.EXAMPLE
    ./scripts/fly-deploy.ps1 -Deploy -App web
#>
[CmdletBinding()]
param(
    [switch] $Setup,
    [switch] $Secrets,
    [switch] $Deploy,
    [ValidateSet('all', 'api', 'web')]
    [string] $App = 'all',
    [string] $Region = 'dfw',
    [string] $EnvFile,
    [switch] $IncludeGitHubToken,
    [switch] $NoCache
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcDir   = Join-Path $RepoRoot 'src'
if (-not $EnvFile) { $EnvFile = Join-Path $RepoRoot '.env' }

# App name, config file, volume, and the secrets each app receives.
$Apps = [ordered]@{
    api = @{
        Name    = 'podslacker-api'
        Config  = 'fly.api.toml'
        Volume  = 'podslacker_data'
        Secrets = @('OPENROUTER_API_KEY', 'OPENAI_API_KEY', 'YOUTUBE_API_KEY', 'PODSLACKER_API_KEY')
    }
    web = @{
        Name    = 'podslacker-web'
        Config  = 'fly.web.toml'
        Volume  = 'dp_keys'
        Secrets = @('PODSLACKER_API_KEY')
    }
}
if ($IncludeGitHubToken) { $Apps.api.Secrets += 'GITHUB_TOKEN' }

# API first: the web UI depends on it.
$Selected = if ($App -eq 'all') { @('api', 'web') } else { @($App) }

function Write-Step([string] $Text) {
    Write-Host ''
    Write-Host "== $Text" -ForegroundColor Cyan
}

function Invoke-Fly {
    Write-Host "  > fly $args" -ForegroundColor DarkYellow
    & fly @args
    if ($LASTEXITCODE -ne 0) { throw "fly $($args[0]) failed (exit code $LASTEXITCODE)." }
}

function Read-EnvFile([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "No settings file at '$Path'. Copy .env.example to .env and fill in your values."
    }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        if ($trimmed -notmatch '^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') { continue }
        $name  = $Matches[1]
        $value = $Matches[2].Trim()
        if ($value.Length -ge 2 -and
            (($value.StartsWith('"') -and $value.EndsWith('"')) -or
             ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        if ($value -ne '') { $values[$name] = $value }
    }
    return $values
}

# ── Preconditions ────────────────────────────────────────────────────────────

if (-not ($Setup -or $Secrets -or $Deploy)) {
    Write-Host 'Nothing to do. Pass one or more of -Setup, -Secrets, -Deploy.' -ForegroundColor Yellow
    Write-Host 'Run  Get-Help ./scripts/fly-deploy.ps1 -Detailed  for details.'
    return
}

if (-not (Get-Command fly -ErrorAction SilentlyContinue)) {
    throw 'The Fly CLI is not installed. See https://fly.io/docs/flyctl/install/'
}

& fly auth whoami *> $null
if ($LASTEXITCODE -ne 0) { throw 'Not logged in to Fly. Run: fly auth login' }

# ── Setup: apps and volumes ──────────────────────────────────────────────────

if ($Setup) {
    Write-Step 'Setup: apps and volumes'

    $existingApps = @(& fly apps list --json | ConvertFrom-Json | ForEach-Object { $_.Name })

    foreach ($key in $Selected) {
        $a = $Apps[$key]

        if ($existingApps -contains $a.Name) {
            Write-Host "  App '$($a.Name)' already exists."
        } else {
            Invoke-Fly apps create $a.Name
        }

        $volumes = @(& fly volumes list --app $a.Name --json | ConvertFrom-Json | ForEach-Object { $_.name })
        if ($volumes -contains $a.Volume) {
            Write-Host "  Volume '$($a.Volume)' already exists on '$($a.Name)'."
        } else {
            Invoke-Fly volumes create $a.Volume --app $a.Name --size 1 --region $Region --yes
        }
    }
}

# ── Secrets ──────────────────────────────────────────────────────────────────

if ($Secrets) {
    Write-Step "Secrets: from $EnvFile"

    $values = Read-EnvFile $EnvFile

    if (-not $values.ContainsKey('OPENROUTER_API_KEY') -and -not $values.ContainsKey('OPENAI_API_KEY')) {
        Write-Warning 'No LLM key in the settings file (OPENROUTER_API_KEY or OPENAI_API_KEY). Jobs will fail at the summary step.'
    }
    if ($values.ContainsKey('GITHUB_TOKEN') -and -not $IncludeGitHubToken) {
        Write-Host '  GITHUB_TOKEN is in the settings file but was not pushed (pass -IncludeGitHubToken to push it).'
    }

    foreach ($key in $Selected) {
        $a = $Apps[$key]
        $names = @($a.Secrets | Where-Object { $values.ContainsKey($_) })

        if ($names.Count -eq 0) {
            Write-Host "  Nothing to set on '$($a.Name)'."
            continue
        }

        Write-Host "  Setting on '$($a.Name)': $($names -join ', ')"
        # --stage: store now, apply on the next deploy. Avoids an extra restart
        # per app, and works before the app has any machines.
        # Built as an explicit array: assigning an if-expression unwraps a
        # one-item array to a string, and splatting a string passes each
        # character as its own argument.
        $importArgs = @('secrets', 'import', '--app', $a.Name)
        if ($Deploy) { $importArgs += '--stage' }
        ($names | ForEach-Object { "$_=$($values[$_])" }) -join "`n" |
            & fly @importArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "fly secrets import failed for '$($a.Name)'." }
    }
}

# ── Deploy ───────────────────────────────────────────────────────────────────

if ($Deploy) {
    Write-Step 'Deploy'

    $branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD).Trim()
    $commit = (& git -C $RepoRoot rev-parse --short HEAD).Trim()
    $dirty  = [bool](& git -C $RepoRoot status --porcelain)
    Write-Host "  Deploying $branch @ $commit$(if ($dirty) { ' (with uncommitted changes)' })"

    # src/ is the Docker build context, and the fly.*.toml files live there.
    Push-Location $SrcDir
    try {
        foreach ($key in $Selected) {
            $a = $Apps[$key]
            $flyArgs = @('deploy', '--config', $a.Config, '--ha=false', '--remote-only')
            if ($NoCache) { $flyArgs += '--no-cache' }
            Invoke-Fly @flyArgs
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    Write-Host '  Done.' -ForegroundColor Green
    if ($Selected -contains 'web') {
        Write-Host "  Web UI: https://$($Apps.web.Name).fly.dev"
    }
    Write-Host "  Logs:   fly logs --app $($Apps.api.Name)"
}
