[CmdletBinding()]
param(
    [string]$MpdevPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') {
    throw 'Dieses Skript benötigt Windows.'
}

$inputDirectory = Join-Path $PSScriptRoot 'input'
$exe = Join-Path $inputDirectory 'EPMRuleCreator.exe'
$license = Join-Path $inputDirectory 'LICENSE'
$helper = Join-Path $PSScriptRoot 'Complete-LocalMsi.ps1'

foreach ($file in @($exe, $license, $helper)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Datei fehlt: $file"
    }
}

$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($metadata.ProductName -ne 'EPMRuleCreator') {
    throw 'Die EXE wurde nicht als EPMRuleCreator erkannt.'
}

$version = '{0}.{1}.{2}' -f `
    $metadata.FileMajorPart,
    $metadata.FileMinorPart,
    $metadata.FileBuildPart

if (
    $metadata.FileMajorPart -lt 1 -or
    $metadata.FileMajorPart -gt 255 -or
    $metadata.FileMinorPart -gt 255 -or
    $metadata.FileBuildPart -gt 65535
) {
    throw 'Die EXE-Version liegt außerhalb der unterstützten MSI-Grenzen.'
}

if (-not $MpdevPath) {
    $command = Get-Command mpdev.exe -ErrorAction SilentlyContinue
    if ($command) {
        $MpdevPath = $command.Source
    } else {
        $MpdevPath = Join-Path $env:ProgramFiles `
            'Master Packager Ltd\Master Packager Dev\mpdev.exe'
    }
}

if (-not (Test-Path -LiteralPath $MpdevPath -PathType Leaf)) {
    throw 'mpdev.exe nicht gefunden. Nutze den Parameter -MpdevPath.'
}

$output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$finalMsi = Join-Path $output 'EPMRuleCreator-win-x64.msi'

if (Test-Path -LiteralPath $finalMsi) {
    throw 'Die Ausgabe existiert bereits. Nutze einen neuen -OutputDirectory.'
}

$build = [IO.Directory]::CreateDirectory(
    (Join-Path $output ('build-' + [guid]::NewGuid().ToString('N')))
).FullName

$raw = [IO.Directory]::CreateDirectory(
    (Join-Path $build 'raw')
).FullName

$package = [ordered]@{
    '$schema' = 'https://www.masterpackager.com/support-master-packager-dev/mpdev-schema.json'
    outputTypes = @('msi')
    outputDirectory = $raw
    outputFileName = 'EPMRuleCreator-win-x64'
    packageName = 'EPMRuleCreator'
    publisher = 'Mathias Borowicz'
    version = $version
    platform = 'x64'
    installDir = (Join-Path $env:ProgramFiles 'EPMRuleCreator')
    icon = $exe
    fileSystemEntries = @(
        @{
            sourcePath = $exe
            targetPath = '$.installDir\EPMRuleCreator.exe'
        },
        @{
            sourcePath = $license
            targetPath = '$.installDir\LICENSE'
        }
    )
    msi = @{
        upgradeCode = '{76D54680-8875-45B9-BD61-755CE052A9AA}'
    }
}

$json = Join-Path $build 'package.json'
$package | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath $json -Encoding UTF8

& $MpdevPath build $json --working-dir $PSScriptRoot

if ($LASTEXITCODE -ne 0) {
    throw "mpdev build fehlgeschlagen. Build-Dateien: $build"
}

$built = Join-Path $raw 'EPMRuleCreator-win-x64.msi'
if (-not (Test-Path -LiteralPath $built -PathType Leaf)) {
    throw "Keine MSI gefunden: $built"
}

Copy-Item -LiteralPath $built -Destination $finalMsi

try {
    & $helper -Path $finalMsi -Version $version
} catch {
    Move-Item -LiteralPath $finalMsi `
        -Destination (Join-Path $build 'FAILED-do-not-install.msi')
    throw
}

$hash = (Get-FileHash -LiteralPath $finalMsi -Algorithm SHA256).Hash
"$hash  EPMRuleCreator-win-x64.msi" |
    Set-Content -LiteralPath (Join-Path $output 'SHA256.txt') -Encoding ASCII

Write-Host "Test-MSI: $finalMsi"
Write-Host "Version: $version"