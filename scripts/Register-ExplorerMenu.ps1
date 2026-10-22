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

$managedDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EPMRuleCreator\App'
$managedPath = Join-Path $managedDirectory 'EPMRuleCreator.exe'
if ($resolvedPath -ine $managedPath) {
    $sameContent = $false
    if (Test-Path -LiteralPath $managedPath -PathType Leaf) {
        $sameContent = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $managedPath -Algorithm SHA256).Hash
    }
    if (-not $sameContent) {
        [IO.Directory]::CreateDirectory($managedDirectory) | Out-Null
        $stagingPath = $managedPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::Copy($resolvedPath, $stagingPath)
            if (Test-Path -LiteralPath $managedPath -PathType Leaf) {
                [IO.File]::Replace($stagingPath, $managedPath, $null)
            } else {
                [IO.File]::Move($stagingPath, $managedPath)
            }
        } finally {
            if (Test-Path -LiteralPath $stagingPath -PathType Leaf) {
                Remove-Item -LiteralPath $stagingPath -Force
            }
        }
    }
}

$extensions = @('.exe', '.msi', '.ps1')
foreach ($extension in $extensions) {
    $key = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\CreateEPMRule"
    New-Item -Path "$key\command" -Force | Out-Null
    Set-Item -LiteralPath $key -Value 'Create EPM Rule'
    Set-ItemProperty -LiteralPath $key -Name 'Icon' -Value ('"{0}",0' -f $managedPath)
    Set-Item -LiteralPath "$key\command" -Value ('"{0}" "%1"' -f $managedPath)
}

foreach ($extension in $extensions) {
    $legacyKey = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\CreateIntuneEpmRule"
    if (Test-Path -LiteralPath $legacyKey) {
        Remove-Item -LiteralPath $legacyKey -Recurse -Force
    }
}

Write-Host 'Explorer menu registered for the current user. In Windows 11 it appears under Show more options.'
Write-Host "Application copy: $managedPath"
