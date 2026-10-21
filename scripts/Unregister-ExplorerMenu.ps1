$ErrorActionPreference = 'Stop'
foreach ($extension in @('.exe', '.msi', '.ps1')) {
    $key = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\CreateIntuneEpmRule"
    if (Test-Path -LiteralPath $key) {
        Remove-Item -LiteralPath $key -Recurse -Force
    }
}
Write-Host 'Explorer menu registration removed for the current user.'
