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
if ([IO.Path]::GetExtension($resolvedPath) -ine '.exe' -or [IO.Path]::GetFileName($resolvedPath) -ieq 'dotnet.exe') {
    throw 'Use the published standalone application EXE.'
}

$extensions = @('.exe', '.msi', '.ps1')
foreach ($extension in $extensions) {
    $key = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\CreateEPMRule"
    New-Item -Path "$key\command" -Force | Out-Null
    Set-Item -LiteralPath $key -Value 'Create EPM Rule'
    Set-ItemProperty -LiteralPath $key -Name 'Icon' -Value ('"{0}",0' -f $resolvedPath)
    Set-Item -LiteralPath "$key\command" -Value ('"{0}" "%1"' -f $resolvedPath)
}

foreach ($extension in $extensions) {
    $legacyKey = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\CreateIntuneEpmRule"
    if (Test-Path -LiteralPath $legacyKey) {
        Remove-Item -LiteralPath $legacyKey -Recurse -Force
    }
}

Write-Host 'Explorer menu registered for the current user. In Windows 11 it appears under Show more options.'
Write-Host "Application: $resolvedPath (no additional EXE copy was created)"
