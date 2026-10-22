# Run in Windows PowerShell 5.1 to cover the .NET Framework ADS regression.
$ErrorActionPreference = 'Stop'
$unblockTestDirectory = Join-Path ([IO.Path]::GetTempPath()) ('EPMRuleCreator-unblock-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($unblockTestDirectory) | Out-Null
$destination = Join-Path $unblockTestDirectory 'verified update [test].exe'
try {
    [IO.File]::WriteAllText($destination, 'verified test contents')
    Set-Content -LiteralPath $destination -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
    $before = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    # Execute the actual command used by the helper, including literal-path handling.
    $unblockCommand = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Install-AppUpdate.ps1') |
        Where-Object { $_ -match '^\s*Unblock-File -LiteralPath \$destination -ErrorAction Stop$' })
    if ($unblockCommand.Count -ne 1) { throw 'Expected exactly one narrowly scoped updater Unblock-File command.' }
    & ([scriptblock]::Create($unblockCommand[0]))
    if (Get-Item -LiteralPath $destination -Stream Zone.Identifier -ErrorAction SilentlyContinue) {
        throw 'The Internet zone marker was not removed.'
    }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $before) {
        throw 'Unblocking changed the executable contents.'
    }
    # Already-unblocked downloads must also succeed.
    & ([scriptblock]::Create($unblockCommand[0]))
    Write-Output 'Update unblock regression test passed in Windows PowerShell.'
} finally {
    Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
    [IO.Directory]::Delete($unblockTestDirectory)
}
