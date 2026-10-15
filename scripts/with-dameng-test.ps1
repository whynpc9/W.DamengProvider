[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Command,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CommandArguments,
    [string] $ExpectedUser = 'WDM_PROVIDER_TEST'
)

$ErrorActionPreference = 'Stop'
if ($ExpectedUser -cne 'WDM_PROVIDER_TEST' -and $ExpectedUser -cnotmatch '\AWDM_PROVIDER_TEST_I11_[A-F0-9]{8}\z') {
    throw 'Expected test identity is not allowed.'
}
$secretPath = Join-Path $PSScriptRoot '../.local/secrets/dameng-test.env'
if (-not (Test-Path -LiteralPath $secretPath -PathType Leaf)) {
    throw 'Local Dameng test secret is missing.'
}

# Parse one assignment as data. Never invoke, dot-source, or print this file.
$secretText = [System.IO.File]::ReadAllText($secretPath)
$assignment = [regex]::Match($secretText, "\Aexport DAMENG_TEST_CONNECTION_STRING='([^'\r\n]*)'\r?\n?\z")
if (-not $assignment.Success) { throw 'Local Dameng test secret format is invalid.' }
$connectionString = $assignment.Groups[1].Value
try {
    $settings = [System.Data.Common.DbConnectionStringBuilder]::new()
    $settings.set_ConnectionString($connectionString)
} catch { throw 'Local Dameng test connection settings are invalid.' }
$userKeys = @($settings.get_Keys() | Where-Object { $_ -in @('User Id', 'User', 'UID', 'Username') })
if ($userKeys.Count -ne 1 -or -not [string]::Equals([string]$settings[$userKeys[0]], $ExpectedUser, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Local Dameng test secret has the wrong account.'
}

$previousConnection = [Environment]::GetEnvironmentVariable('DAMENG_TEST_CONNECTION_STRING', 'Process')
$previousExpectedUser = [Environment]::GetEnvironmentVariable('DAMENG_TEST_EXPECTED_USER', 'Process')
$exitCode = 1
try {
    [Environment]::SetEnvironmentVariable('DAMENG_TEST_CONNECTION_STRING', $connectionString, 'Process')
    [Environment]::SetEnvironmentVariable('DAMENG_TEST_EXPECTED_USER', $ExpectedUser, 'Process')
    $global:LASTEXITCODE = 0
    & $Command @CommandArguments
    $exitCode = $LASTEXITCODE
} finally {
    [Environment]::SetEnvironmentVariable('DAMENG_TEST_CONNECTION_STRING', $previousConnection, 'Process')
    [Environment]::SetEnvironmentVariable('DAMENG_TEST_EXPECTED_USER', $previousExpectedUser, 'Process')
    $connectionString = $null
    $secretText = $null
    $assignment = $null
    $settings = $null
}
exit $exitCode
