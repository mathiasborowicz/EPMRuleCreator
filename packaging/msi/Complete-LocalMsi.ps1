[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') {
    throw 'Windows Installer COM benötigt Windows.'
}

$Path = (Resolve-Path -LiteralPath $Path).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null

function Call-Com($Object, [string]$Name, [object[]]$Arguments = @()) {
    $Object.GetType().InvokeMember(
        $Name, [Reflection.BindingFlags]::InvokeMethod,
        $null, $Object, $Arguments
    )
}

function Get-Com($Object, [string]$Name, [object[]]$Arguments = @()) {
    $Object.GetType().InvokeMember(
        $Name, [Reflection.BindingFlags]::GetProperty,
        $null, $Object, $Arguments
    )
}

function Set-Com($Object, [string]$Name, [object[]]$Arguments) {
    [void]$Object.GetType().InvokeMember(
        $Name, [Reflection.BindingFlags]::SetProperty,
        $null, $Object, $Arguments
    )
}

function Sql([string]$Query, [object[]]$Values = @(), [switch]$Read) {
    $view = Call-Com $database 'OpenView' @($Query)
    $parameters = $null
    try {
        if ($Values.Count -gt 0) {
            $parameters = Call-Com $installer 'CreateRecord' @($Values.Count)
            for ($i = 0; $i -lt $Values.Count; $i++) {
                if ($null -eq $Values[$i]) { continue }
                if ($Values[$i] -is [int]) {
                    Set-Com $parameters 'IntegerData' @(($i + 1), $Values[$i])
                } else {
                    Set-Com $parameters 'StringData' @(($i + 1), [string]$Values[$i])
                }
            }
            [void](Call-Com $view 'Execute' @($parameters))
        } else {
            [void](Call-Com $view 'Execute')
        }

        if ($Read) {
            while ($true) {
                $record = Call-Com $view 'Fetch'
                if ($null -eq $record) { break }
                try {
                    $data = @()
                    for ($i = 1; $i -le (Get-Com $record 'FieldCount'); $i++) {
                        $data += [string](Get-Com $record 'StringData' @($i))
                    }
                    [pscustomobject]@{ Data = $data }
                } finally {
                    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
                }
            }
        }
    } finally {
        [void](Call-Com $view 'Close')
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        if ($parameters) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($parameters)
        }
    }
}

function Table([string]$Name, [string]$Definition) {
    if (@(Sql 'SELECT `Name` FROM `_Tables` WHERE `Name` = ?' @($Name) -Read).Count -eq 0) {
        Sql $Definition
    }
}

function Property([string]$Name, [string]$Value) {
    Sql 'DELETE FROM `Property` WHERE `Property` = ?' @($Name)
    Sql 'INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)' @($Name, $Value)
}

function Set-MsiSequence(
    [string]$Table,
    [string]$Action,
    [string]$Condition,
    [int]$Number
) {
    Sql ('DELETE FROM `' + $Table + '` WHERE `Action` = ?') @($Action)
    Sql ('INSERT INTO `' + $Table + '` (`Action`, `Condition`, `Sequence`) VALUES (?, ?, ?)') @($Action, $Condition, $Number)
}

function Control(
    [string]$Dialog,
    [string]$Name,
    [string]$Type,
    [int]$X,
    [int]$Y,
    [int]$Width,
    [int]$Height,
    $Property,
    $Text,
    $Next
) {
    Sql 'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)' @($Dialog, $Name, $Type, $X, $Y, $Width, $Height, 3, $Property, $Text, $Next)
}

try {
    $database = Call-Com $installer 'OpenDatabase' @($Path, 1)

    $productName = @(Sql 'SELECT `Value` FROM `Property` WHERE `Property` = ?' @('ProductName') -Read)
    $productVersion = @(Sql 'SELECT `Value` FROM `Property` WHERE `Property` = ?' @('ProductVersion') -Read)
    $upgrade = @(Sql 'SELECT `Value` FROM `Property` WHERE `Property` = ?' @('UpgradeCode') -Read)

    if (
        $productName.Count -ne 1 -or
        $productVersion.Count -ne 1 -or
        $upgrade.Count -ne 1 -or
        $productName[0].Data[0] -ne 'EPMRuleCreator' -or
        $productVersion[0].Data[0] -ne $Version -or
        $upgrade[0].Data[0] -ne '{76D54680-8875-45B9-BD61-755CE052A9AA}'
    ) {
        throw 'Unerwartete MSI-Identität. Keine Änderung durchgeführt.'
    }

    $exe = @(Sql 'SELECT `File`, `Component_`, `FileName` FROM `File`' -Read |
        Where-Object { ($_.Data[2] -split '\|')[-1] -eq 'EPMRuleCreator.exe' })

    if ($exe.Count -ne 1) {
        throw 'Die MSI muss genau eine EPMRuleCreator.exe enthalten.'
    }

    $component = $exe[0].Data[1]
    $componentRow = @(Sql 'SELECT `Directory_`, `Attributes` FROM `Component` WHERE `Component` = ?' @($component) -Read)
    $originalDirectory = $componentRow[0].Data[0]
    $appDirectory = 'EPMINSTALLDIR'

    if (@(Sql 'SELECT `Directory` FROM `Directory` WHERE `Directory` = ?' @($appDirectory) -Read).Count) {
        throw 'Die MSI wurde bereits angepasst. Einen frischen Build verwenden.'
    }

    $otherDirectories = @(Sql 'SELECT `Directory_` FROM `Component`' -Read |
        Where-Object { $_.Data[0] -ne $originalDirectory })

    if ($otherDirectories.Count) {
        throw 'Unerwartete Komponenten außerhalb des App-Ordners. Build abbrechen und die rohe MSI prüfen.'
    }

    Sql 'INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES (?, ?, ?)' @($appDirectory, 'TARGETDIR', 'EPMRuleCreator')
    Sql 'UPDATE `Component` SET `Directory_` = ? WHERE `Directory_` = ?' @($appDirectory, $originalDirectory)

    if (@(Sql 'SELECT `Name` FROM `_Tables` WHERE `Name` = ?' @('CreateFolder') -Read).Count) {
        $folders = @(Sql 'SELECT `Component_` FROM `CreateFolder` WHERE `Directory_` = ?' @($originalDirectory) -Read)
        Sql 'DELETE FROM `CreateFolder` WHERE `Directory_` = ?' @($originalDirectory)
        foreach ($folder in $folders) {
            Sql 'INSERT INTO `CreateFolder` (`Directory_`, `Component_`) VALUES (?, ?)' @($appDirectory, $folder.Data[0])
        }
    }

    if (@(Sql 'SELECT `Name` FROM `_Tables` WHERE `Name` = ?' @('RemoveFile') -Read).Count) {
        Sql 'UPDATE `RemoveFile` SET `DirProperty` = ? WHERE `DirProperty` = ?' @($appDirectory, $originalDirectory)
    }

    Sql 'UPDATE `Component` SET `ComponentId` = ?, `Attributes` = ? WHERE `Component` = ?' @('{4B4CB50B-2F8E-416C-B5DD-23508F9C1BF8}', ([int]$componentRow[0].Data[1] -bor 256), $component)

    Property 'ALLUSERS' '2'
    Property 'MSIINSTALLPERUSER' '1'
    Property 'EPM_INSTALL_SCOPE' 'perUser'
    Property 'ARPNOMODIFY' '1'
    Property 'ARPURLINFOABOUT' 'https://github.com/mathiasborowicz/EPMRuleCreator'
    Property 'ARPHELPLINK' 'https://github.com/mathiasborowicz/EPMRuleCreator/issues'
    Property 'DefaultUIFont' 'EpmBody'

    $secure = @(Sql 'SELECT `Value` FROM `Property` WHERE `Property` = ?' @('SecureCustomProperties') -Read)
    $secureNames = @(
        'EPM_PREVIOUS', 'EPM_NEWER', 'EPM_INSTALL_SCOPE',
        'MSIINSTALLPERUSER', 'EPM_EXISTING_USER', 'EPM_EXISTING_MACHINE'
    )
    if ($secure.Count) { $secureNames += $secure[0].Data[0] -split ';' }
    Property 'SecureCustomProperties' (($secureNames | Select-Object -Unique) -join ';')

    Table 'InstallUISequence' 'CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)'
    Table 'Dialog' 'CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50), `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)'
    Table 'Control' 'CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(72), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)'
    Table 'ControlEvent' 'CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)'
    Table 'RadioButton' 'CREATE TABLE `RadioButton` (`Property` CHAR(72) NOT NULL, `Order` SHORT NOT NULL, `Value` CHAR(64) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Text` CHAR(64) LOCALIZABLE, `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Property`, `Order`)'
    Table 'TextStyle' 'CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL LOCALIZABLE, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)'

    Sql 'INSERT INTO `TextStyle` (`TextStyle`, `FaceName`, `Size`, `StyleBits`) VALUES (?, ?, ?, ?)' @('EpmBody', 'Segoe UI', 9, 0)
    Sql 'INSERT INTO `TextStyle` (`TextStyle`, `FaceName`, `Size`, `StyleBits`) VALUES (?, ?, ?, ?)' @('EpmTitle', 'Segoe UI', 13, 1)

    Sql 'INSERT INTO `Dialog` (`Dialog`, `HCentering`, `VCentering`, `Width`, `Height`, `Attributes`, `Title`, `Control_First`, `Control_Default`, `Control_Cancel`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)' @('EpmScope', 50, 50, 370, 220, 3, 'EPMRuleCreator [ProductVersion]', 'Scope', 'Install', 'Cancel')

    Control 'EpmScope' 'Title' 'Text' 20 15 330 30 $null '{\EpmTitle}Install EPMRuleCreator' $null
    Control 'EpmScope' 'Description' 'Text' 20 52 330 20 $null 'Who should be able to use this app?' $null
    Control 'EpmScope' 'Scope' 'RadioButtonGroup' 20 78 330 65 'EPM_INSTALL_SCOPE' $null 'Install'

    Sql 'INSERT INTO `RadioButton` (`Property`, `Order`, `Value`, `X`, `Y`, `Width`, `Height`, `Text`) VALUES (?, ?, ?, ?, ?, ?, ?, ?)' @('EPM_INSTALL_SCOPE', 1, 'perUser', 5, 5, 310, 18, 'Only for me')
    Sql 'INSERT INTO `RadioButton` (`Property`, `Order`, `Value`, `X`, `Y`, `Width`, `Height`, `Text`) VALUES (?, ?, ?, ?, ?, ?, ?, ?)' @('EPM_INSTALL_SCOPE', 2, 'perMachine', 5, 34, 310, 18, 'All users (requires admin permissions)')

    Control 'EpmScope' 'Info' 'Text' 20 150 330 20 $null 'A Start menu shortcut will be created.' $null
    Control 'EpmScope' 'Install' 'PushButton' 192 184 75 24 $null '&Install' 'Cancel'
    Control 'EpmScope' 'Cancel' 'PushButton' 275 184 75 24 $null 'Cancel' 'Scope'

    foreach ($event in @(
        @('Install', '[MSIINSTALLPERUSER]', '1', 'EPM_INSTALL_SCOPE="perUser"', 1),
        @('Install', '[MSIINSTALLPERUSER]', '{}', 'EPM_INSTALL_SCOPE="perMachine"', 2),
        @('Install', 'EndDialog', 'Return', '1', 3),
        @('Cancel', 'EndDialog', 'Exit', '1', 1)
    )) {
        Sql 'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (?, ?, ?, ?, ?, ?)' @('EpmScope', $event[0], $event[1], $event[2], $event[3], [int]$event[4])
    }

    foreach ($ending in @(
        @('EpmSuccess', -1, 'Installation completed.'),
        @('EpmCancelled', -2, 'Installation cancelled.'),
        @('EpmFailure', -3, 'Installation failed. See the MSI log for details.')
    )) {
        $dialog = $ending[0]
        Sql 'INSERT INTO `Dialog` (`Dialog`, `HCentering`, `VCentering`, `Width`, `Height`, `Attributes`, `Title`, `Control_First`, `Control_Default`, `Control_Cancel`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)' @($dialog, 50, 50, 370, 150, 3, 'EPMRuleCreator', 'Close', 'Close', 'Close')
        Control $dialog 'Result' 'Text' 20 25 330 60 $null $ending[2] $null
        Control $dialog 'Close' 'PushButton' 275 110 75 24 $null 'Close' $null
        Sql 'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (?, ?, ?, ?, ?, ?)' @($dialog, 'Close', 'EndDialog', 'Return', '1', 1)
        Set-MsiSequence 'InstallUISequence' $dialog '' ([int]$ending[1])
    }

    foreach ($action in @(
        @('EpmUserDir', 35, $appDirectory, '[LocalAppDataFolder]Programs\EPMRuleCreator\', 'NOT ALLUSERS', 1010),
        @('EpmMachineDir', 35, $appDirectory, '[ProgramFiles64Folder]EPMRuleCreator\', 'ALLUSERS=1', 1020),
        @('EpmInstallLocation', 51, 'ARPINSTALLLOCATION', ('[' + $appDirectory + ']'), '1', 1050),
        @('EpmExistingMachineScope', 51, 'EPM_INSTALL_SCOPE', 'perMachine', 'EPM_EXISTING_MACHINE', 110)
    )) {
        Sql 'INSERT INTO `CustomAction` (`Action`, `Type`, `Source`, `Target`) VALUES (?, ?, ?, ?)' @($action[0], [int]$action[1], $action[2], $action[3])
        foreach ($sequence in @('InstallUISequence', 'InstallExecuteSequence')) {
            Set-MsiSequence $sequence $action[0] $action[4] ([int]$action[5])
        }
    }

    Set-MsiSequence 'InstallUISequence' 'EpmScope' 'NOT Installed AND NOT REMOVE' 750

    foreach ($step in @(
        @('FindRelatedProducts', 25),
        @('AppSearch', 50),
        @('CostInitialize', 800),
        @('FileCost', 900),
        @('CostFinalize', 1000),
        @('LaunchConditions', 1200)
    )) {
        Set-MsiSequence 'InstallUISequence' $step[0] '1' ([int]$step[1])
    }

    Set-MsiSequence 'InstallUISequence' 'ExecuteAction' '1' 1300
    Set-MsiSequence 'InstallExecuteSequence' 'LaunchConditions' '1' 1200
    Set-MsiSequence 'InstallExecuteSequence' 'FindRelatedProducts' '1' 25
    Set-MsiSequence 'InstallExecuteSequence' 'AppSearch' '1' 50

    Table 'Registry' 'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)'

    foreach ($value in @(
        @('ProductCode', '[ProductCode]'),
        @('ExecutablePath', ('[' + $appDirectory + ']EPMRuleCreator.exe')),
        @('Version', '[ProductVersion]')
    )) {
        Sql 'INSERT INTO `Registry` (`Registry`, `Root`, `Key`, `Name`, `Value`, `Component_`) VALUES (?, ?, ?, ?, ?, ?)' @(('Epm' + $value[0]), -1, 'Software\EPMRuleCreator\Installer', $value[0], $value[1], $component)
    }

    # AppSearch queries Signature while resolving locator rows, even when those rows intentionally search registry values rather than files.
    Table 'Signature' 'CREATE TABLE `Signature` (`Signature` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, `MinVersion` CHAR(20), `MaxVersion` CHAR(20), `MinSize` LONG, `MaxSize` LONG, `MinDate` LONG, `MaxDate` LONG, `Languages` CHAR(255) PRIMARY KEY `Signature`)'
    Table 'RegLocator' 'CREATE TABLE `RegLocator` (`Signature_` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL, `Name` CHAR(255), `Type` SHORT PRIMARY KEY `Signature_`)'
    Table 'AppSearch' 'CREATE TABLE `AppSearch` (`Property` CHAR(72) NOT NULL, `Signature_` CHAR(72) NOT NULL PRIMARY KEY `Property`, `Signature_`)'

    foreach ($search in @(@('USER', 1), @('MACHINE', 2))) {
        $signature = 'EpmInstalled' + $search[0]
        Sql 'INSERT INTO `RegLocator` (`Signature_`, `Root`, `Key`, `Name`, `Type`) VALUES (?, ?, ?, ?, ?)' @($signature, [int]$search[1], 'Software\EPMRuleCreator\Installer', 'ExecutablePath', 18)
        Sql 'INSERT INTO `AppSearch` (`Property`, `Signature_`) VALUES (?, ?)' @(('EPM_EXISTING_' + $search[0]), $signature)
    }

    # Type 18 means a raw registry value in the 64-bit registry view; keeping these signatures out of Signature preserves that behavior.
    $scopeSearches = @(Sql 'SELECT `AppSearch`.`Property`, `RegLocator`.`Root`, `RegLocator`.`Type` FROM `AppSearch`, `RegLocator` WHERE `AppSearch`.`Signature_` = `RegLocator`.`Signature_`' -Read)
    if ($scopeSearches.Count -ne 2) {
        throw 'Die MSI muss genau zwei verknüpfte AppSearch-/RegLocator-Scope-Suchen enthalten.'
    }

    foreach ($expectedSearch in @(
        @('EPM_EXISTING_USER', '1', '18'),
        @('EPM_EXISTING_MACHINE', '2', '18')
    )) {
        $matchingSearch = @($scopeSearches | Where-Object { $_.Data[0] -eq $expectedSearch[0] })
        if (
            $matchingSearch.Count -ne 1 -or
            $matchingSearch[0].Data[1] -ne $expectedSearch[1] -or
            $matchingSearch[0].Data[2] -ne $expectedSearch[2]
        ) {
            throw "Unerwartete Registry-Suche für $($expectedSearch[0])."
        }
    }

    $fileSignatures = @(Sql 'SELECT `Signature` FROM `Signature`' -Read |
        Where-Object { $_.Data[0] -in @('EpmInstalledUSER', 'EpmInstalledMACHINE') })
    if ($fileSignatures.Count -ne 0) {
        throw 'Die Scope-Suchen müssen Registry-Werte lesen und dürfen nicht als Dateisignaturen definiert sein.'
    }

    Table 'LaunchCondition' 'CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)'

    Sql 'INSERT INTO `LaunchCondition` (`Condition`, `Description`) VALUES (?, ?)' @('Installed OR NOT EPM_NEWER', 'The same or a newer MSI version is already installed.')
    Sql 'INSERT INTO `LaunchCondition` (`Condition`, `Description`) VALUES (?, ?)' @('Installed OR REMOVE OR ((NOT EPM_EXISTING_USER OR NOT ALLUSERS) AND (NOT EPM_EXISTING_MACHINE OR ALLUSERS=1))', 'EPMRuleCreator is already installed for another scope. Uninstall it before changing between Only for me and All users.')

    Table 'Upgrade' 'CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)'

    Sql 'DELETE FROM `Upgrade`'
    Sql 'INSERT INTO `Upgrade` (`UpgradeCode`, `VersionMax`, `Attributes`, `Remove`, `ActionProperty`) VALUES (?, ?, ?, ?, ?)' @('{76D54680-8875-45B9-BD61-755CE052A9AA}', $Version, 1, 'ALL', 'EPM_PREVIOUS')
    Sql 'INSERT INTO `Upgrade` (`UpgradeCode`, `VersionMin`, `Attributes`, `ActionProperty`) VALUES (?, ?, ?, ?)' @('{76D54680-8875-45B9-BD61-755CE052A9AA}', $Version, 258, 'EPM_NEWER')

    Set-MsiSequence 'InstallExecuteSequence' 'RemoveExistingProducts' 'EPM_PREVIOUS' 1510

    Table 'Shortcut' 'CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)'

    if (@(Sql 'SELECT `Directory` FROM `Directory` WHERE `Directory` = ?' @('ProgramMenuFolder') -Read).Count -eq 0) {
        Sql 'INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES (?, ?, ?)' @('ProgramMenuFolder', 'TARGETDIR', '.')
    }

    Sql 'INSERT INTO `Shortcut` (`Shortcut`, `Directory_`, `Name`, `Component_`, `Target`, `Description`, `ShowCmd`, `WkDir`) VALUES (?, ?, ?, ?, ?, ?, ?, ?)' @('EpmStartMenu', 'ProgramMenuFolder', 'EPMRuleCreator', $component, ('[' + $appDirectory + ']EPMRuleCreator.exe'), 'EPMRuleCreator', 1, $appDirectory)

    Set-MsiSequence 'InstallExecuteSequence' 'CreateShortcuts' '1' 4500
    Set-MsiSequence 'InstallExecuteSequence' 'WriteRegistryValues' '1' 5000
    Set-MsiSequence 'InstallExecuteSequence' 'RemoveShortcuts' '1' 3200
    Set-MsiSequence 'InstallExecuteSequence' 'RemoveRegistryValues' '1' 2600

    $summary = Get-Com $database 'SummaryInformation' @(20)
    try {
        $template = [string](Get-Com $summary 'Property' @(7))
        if ($template -notmatch '^x64;') {
            throw "Erwartet wurde eine x64-MSI, gefunden: $template"
        }

        $wordCount = [int](Get-Com $summary 'Property' @(15))
        Set-Com $summary 'Property' @(15, ($wordCount -band (-bnot 8)))
        [void](Call-Com $summary 'Persist')
    } finally {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
    }

    [void](Call-Com $database 'Commit')
    Write-Host 'Installationsauswahl und Registry-Einträge ergänzt.'
} finally {
    if ($database) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
