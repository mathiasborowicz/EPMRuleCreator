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
$installedPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EPMRuleCreator\App\EPMRuleCreator.exe'
function Test-MsiApplicationPath {
    <#
    .SYNOPSIS
        Checks whether an executable is the registered MSI application.

    .DESCRIPTION
        Reads the 64-bit MSI marker and matching Windows Installer uninstall entry
        from both the current-user and local-machine registry hives.
    #>
    param([Parameter(Mandatory)][string] $Path)

    $installerKeyPath = 'Software\EPMRuleCreator\Installer'
    $uninstallKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall'
    foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine)) {
        $root = $null
        $installer = $null
        $uninstallRoot = $null
        $uninstallEntry = $null
        try {
            $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)
            $installer = $root.OpenSubKey($installerKeyPath)
            if ($null -eq $installer) { continue }
            $registeredPath = [string]$installer.GetValue('ExecutablePath')
            $productCode = [string]$installer.GetValue('ProductCode')
            if ([string]::IsNullOrWhiteSpace($registeredPath) -or [string]::IsNullOrWhiteSpace($productCode)) { continue }
            $uninstallRoot = $root.OpenSubKey($uninstallKeyPath)
            if ($null -eq $uninstallRoot) { continue }
            $uninstallEntry = $uninstallRoot.OpenSubKey($productCode)
            if ($null -eq $uninstallEntry) { continue }
            $uninstallProductCode = [string]$uninstallEntry.GetValue('ProductCode')
            if ([string]::IsNullOrWhiteSpace($uninstallProductCode)) { $uninstallProductCode = $productCode }
            $normalizedPath = [IO.Path]::GetFullPath($Path.Trim().Trim('"'))
            $normalizedRegisteredPath = [IO.Path]::GetFullPath($registeredPath.Trim().Trim('"'))
            $normalizedProductCode = $productCode.Trim().Trim('{}')
            $normalizedUninstallProductCode = $uninstallProductCode.Trim().Trim('{}')
            if ($normalizedPath -ieq $normalizedRegisteredPath -and $normalizedProductCode -ieq $normalizedUninstallProductCode) { return $true }
        }
        catch [System.IO.IOException] { }
        catch [System.UnauthorizedAccessException] { }
        catch [System.Security.SecurityException] { }
        finally {
            if ($null -ne $uninstallEntry) { $uninstallEntry.Dispose() }
            if ($null -ne $uninstallRoot) { $uninstallRoot.Dispose() }
            if ($null -ne $installer) { $installer.Dispose() }
            if ($null -ne $root) { $root.Dispose() }
        }
    }
    return $false
}

if ($resolvedPath -ine $installedPath) {
    if (-not (Test-MsiApplicationPath -Path $resolvedPath)) {
        throw 'Explorer integration is available only for an MSI installation, not the portable version.'
    }
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
