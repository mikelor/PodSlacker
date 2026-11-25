<#
.SYNOPSIS
    Loads PodSlacker settings from a .env file into the current PowerShell session.

.DESCRIPTION
    Reads KEY=VALUE lines from a .env file (repo root by default) and sets each
    one as an environment variable for this PowerShell session. Anything started
    from the same session afterwards (the Aspire AppHost, the CLI, the API
    service) inherits them.

    The .env file is git-ignored. Copy .env.example to .env to get started.

    Values are never printed; the script only reports which names were set.

.PARAMETER Path
    Path to the .env file. Defaults to .env in the repository root.

.PARAMETER Persist
    Also save the variables to your Windows user profile so new terminals get
    them without running this script again.

.EXAMPLE
    ./scripts/Set-PodSlackerEnv.ps1
    dotnet run --project src/PodSlacker.AppHost

.EXAMPLE
    ./scripts/Set-PodSlackerEnv.ps1 -Persist
#>
[CmdletBinding()]
param(
    [string] $Path = (Join-Path (Split-Path -Parent $PSScriptRoot) '.env'),
    [switch] $Persist
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    $example = Join-Path (Split-Path -Parent $PSScriptRoot) '.env.example'
    Write-Error "No settings file found at '$Path'. Copy '$example' to '$Path' and fill in your values."
    return
}

$set     = [System.Collections.Generic.List[string]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()
$lineNumber = 0

foreach ($line in Get-Content -LiteralPath $Path) {
    $lineNumber++
    $trimmed = $line.Trim()

    if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }

    # Allow an optional leading "export " so the file also works with bash.
    if ($trimmed -notmatch '^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        Write-Warning "Skipping line $lineNumber of '$Path': not in KEY=VALUE form."
        continue
    }

    $name  = $Matches[1]
    $value = $Matches[2].Trim()

    # Strip one pair of matching surrounding quotes.
    if ($value.Length -ge 2 -and
        (($value.StartsWith('"') -and $value.EndsWith('"')) -or
         ($value.StartsWith("'") -and $value.EndsWith("'")))) {
        $value = $value.Substring(1, $value.Length - 2)
    }

    if ($value -eq '') {
        $skipped.Add($name)
        continue
    }

    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    if ($Persist) {
        [Environment]::SetEnvironmentVariable($name, $value, 'User')
    }
    $set.Add($name)
}

if ($set.Count -gt 0) {
    $scope = if ($Persist) { 'this session and your user profile' } else { 'this session' }
    Write-Host "Set for ${scope}: $($set -join ', ')"
}
if ($skipped.Count -gt 0) {
    Write-Host "Left unset (empty in file): $($skipped -join ', ')"
}
if (-not $set.Contains('OPENROUTER_API_KEY') -and -not $set.Contains('OPENAI_API_KEY')) {
    Write-Warning 'No LLM key was set (OPENROUTER_API_KEY or OPENAI_API_KEY). Jobs will fail at the summary step.'
}
