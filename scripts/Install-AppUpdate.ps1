# Embedded in the EXE and invoked with literal destination, stage and appProcessId values.
# This helper never elevates and never changes the installation path or user settings.
$ErrorActionPreference = 'Stop'
$stagedExe = Join-Path $stage 'EPMRuleCreator.exe'
$replacement = $destination + '.update-' + [Guid]::NewGuid().ToString('N') + '.tmp'
$backup = $destination + '.previous'
$replaced = $false
try {
    [IO.File]::WriteAllText((Join-Path $stage 'helper-ready'), 'ready')
    $app = Get-Process -Id $appProcessId -ErrorAction SilentlyContinue
    if ($null -ne $app -and -not $app.WaitForExit(60000)) { throw 'The app did not close. No update was installed.' }
    [IO.File]::Copy($stagedExe, $replacement, $false)
    [IO.File]::Replace($replacement, $destination, $backup)
    $replaced = $true
    # ReplaceFile may preserve named streams from the previous EXE.
    # Unblock only this verified replacement, never a directory or unrelated files.
    # Windows PowerShell 5.1 uses .NET Framework, whose File.Delete rejects ADS paths.
    Unblock-File -LiteralPath $destination -ErrorAction Stop
    Start-Process -FilePath $destination -WorkingDirectory ([IO.Path]::GetDirectoryName($destination)) -ErrorAction Stop
    try {
        Remove-Item -LiteralPath $stagedExe -Force
        Remove-Item -LiteralPath (Join-Path $stage 'update.zip') -Force
        Remove-Item -LiteralPath (Join-Path $stage 'helper-ready') -Force
        Set-Location ([IO.Path]::GetTempPath())
        [IO.Directory]::Delete($stage)
    } catch { } # Cleanup failure must never roll back a successfully started update.
} catch {
    $failure = $_.Exception.Message
    if ($replaced) {
        try {
            [IO.File]::Copy($backup, $destination, $true)
            Start-Process -FilePath $destination
        } catch { $failure += "`nRollback: " + $_.Exception.Message }
    }
    if ([IO.File]::Exists($replacement)) { [IO.File]::Delete($replacement) }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("Update failed: $failure`nPrevious EXE: $backup", 'EPMRuleCreator update') | Out-Null
}
