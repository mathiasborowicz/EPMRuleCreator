[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ApplicationPath
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $ApplicationPath).Path
if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
    throw "Application executable was not found: $ApplicationPath"
}

$extensions = @('.exe', '.msi', '.ps1')
foreach ($extension in $extensions) {
    $key = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\CreateIntuneEpmRule"
    New-Item -Path "$key\command" -Force | Out-Null
    Set-Item -LiteralPath $key -Value 'Create Intune EPM Rule'
    Set-ItemProperty -LiteralPath $key -Name 'Icon' -Value $resolvedPath
    Set-Item -LiteralPath "$key\command" -Value ('"{0}" "%1"' -f $resolvedPath)
}

Write-Host 'Explorer menu registered for the current user. In Windows 11 it appears under Show more options.'
