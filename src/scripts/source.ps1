// Source - https://stackoverflow.com/a/72001469
// Posted by nc1943
// Retrieved 2026-05-05, License - CC BY-SA 4.0

// Run the script passing the path to your .env file: C:> ./source.ps1 ./.env -Verbose
// Include optional params:
//   -Verbose to see what environment variables were se
//   -Remove to delete all the environment variables with names found in that .env file
//   -RemoveQuotes to strip the " and ' from surrounding the value side of the key/value pairs in your .env before making environment variables out of them



param(
    [string]$Path,
    [switch]$Verbose,
    [switch]$Remove,
    [switch]$RemoveQuotes
)

$variables = Select-String -Path $Path -Pattern '^\s*[^\s=#]+=[^\s]+$' -Raw

foreach($var in $variables) {
    $keyVal = $var -split '=', 2
    $key = $keyVal[0].Trim()
    $val = $RemoveQuotes ? $keyVal[1].Trim("'").Trim('"') : $keyVal[1]
    [Environment]::SetEnvironmentVariable($key, $Remove ? '' : $val)
    if ($Verbose) {
        "$key=$([Environment]::GetEnvironmentVariable($key))"
    }
}
