$ErrorActionPreference = 'Stop'
foreach ($extension in @('.exe', '.msi', '.ps1')) {
    foreach ($menuName in @('CreateEPMRule', 'CreateIntuneEpmRule')) {
        $key = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\$menuName"
        if (Test-Path -LiteralPath $key) {
            Remove-Item -LiteralPath $key -Recurse -Force
        }
    }
}
Write-Host 'Explorer menu registration removed for the current user.'
