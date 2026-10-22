# Installation and Explorer integration

## Requirements

- Windows 11 x64.
- Git for Windows to obtain the source branch.
- .NET 10 SDK only if building locally. The published application is self-contained and does not need a separate .NET runtime.
- Microsoft Intune Endpoint Privilege Management Tools are optional for basic offline file information. They are needed only for `Get-FileAttributes` output. Obtain them through your organization's supported Microsoft Intune deployment; the application does not install the EPM agent or change device configuration.

## Get the project

The development branch is `work`. First inspect the intended folder so an existing directory is not overwritten:

```powershell
Get-ChildItem -Force C:\Git\EPMRuleCreator
```

If that directory is empty, clone into it:

```powershell
git clone --branch work --single-branch https://github.com/mathiasborowicz/EPMRuleCreator.git C:\Git\EPMRuleCreator
Set-Location C:\Git\EPMRuleCreator
```

If it contains files, keep them and clone into a new empty directory instead, then use that directory in the remaining commands.

## Install the SDK

Install the **SDK**, not only the runtime. With `winget`:

```powershell
winget install --exact --id Microsoft.DotNet.SDK.10
```

Alternatively, use the [official .NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0) and select the Windows x64 SDK. Close and reopen PowerShell, then verify:

```powershell
dotnet --list-sdks
```

At least one `10.0.x` SDK should be listed.

## Build and publish

Run from the repository root:

```powershell
dotnet test .\IntuneEpmRuleCreator.sln
dotnet publish .\src\IntuneEpmRuleCreator.UI\IntuneEpmRuleCreator.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true -o .\publish\win-x64
```

The executable is `publish\win-x64\IntuneEpmRuleCreator.exe`. Run it directly to select a file in the app. No administrator rights, tenant connection, or network are needed for local file analysis.

## Download the current standalone build

For normal use, download [**IntuneEpmRuleCreator.exe from the latest GitHub release**](https://github.com/mathiasborowicz/EPMRuleCreator/releases/latest/download/IntuneEpmRuleCreator.exe). This is a direct EXE download; you do not need the source ZIP, Git, or the .NET SDK. A release is created automatically after a push/merge to `main` passes tests and publishes the Windows build. Releases use version tags starting at `v0.9.0` and are not subject to the 30-day Actions-artifact retention. Further merges automatically increment the highest version's patch number, and the same version is embedded in the EXE. A higher `<Version>` in the UI project starts a new version line. Retrying an already tagged commit reuses its release instead of incrementing again. Failed builds and pull-request checks do not publish releases. Older `build-*` releases remain unchanged.

Every push to `work` or `main` runs tests and creates a self-contained, compressed Windows x64 build automatically. The single executable is about 63 MB in the current build (down from about 127 MB); WPF's native components are bundled and extracted by .NET at startup. To download it, open the repository's **Actions** tab, select the latest successful **Windows standalone build**, and download the `IntuneEpmRuleCreator-win-x64` artifact. GitHub provides the artifact as a ZIP containing only `IntuneEpmRuleCreator.exe`; no source project or companion files are included. GitHub retains each artifact for 30 days. The build does not embed an Entra client ID; click the circular account button with the person icon at the top right to enter the client ID and tenant if you want to test Graph sign-in. Compression and extraction may add a small startup cost.

## Register the Explorer menu

Start the downloaded standalone EXE and click **Add menu** in the top-right header. The app copies the running EXE to `%LOCALAPPDATA%\IntuneEpmRuleCreator\App\IntuneEpmRuleCreator.exe`, registers that fixed path, and refreshes Explorer's association cache. You can then move or delete the downloaded EXE without breaking the menu. The managed copy must remain in Local AppData. No administrator rights or companion files are required.

The button changes to **Remove menu**, with a small **↻** repair/update button beside it. If you registered an earlier version at its download path, open the new EXE and click **↻** once to migrate the registration. To update later, open the newly downloaded EXE and click **↻** again. Close other instances launched from Explorer first. The complete replacement is staged before replacing the managed file, and registration is changed only after deployment succeeds. Identical copies and registration from the managed location do not rewrite the EXE.

The PowerShell scripts are an alternative. From a normal, non-elevated PowerShell session in the repository root, register the current user's menu:

```powershell
$appPath = Join-Path $PWD 'publish\win-x64\IntuneEpmRuleCreator.exe'
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Register-ExplorerMenu.ps1 -ApplicationPath $appPath
```

The registration is per-user under `HKEY_CURRENT_USER` and does not require administrator privileges. Windows 11 shows this classic registry menu item under **Show more options**; it is not in the primary modern context menu. The menu is registered for `.exe`, `.msi`, and `.ps1` files. Right-click one of those files, choose **Show more options**, then **Create Intune EPM Rule**. The application starts with that path and analyzes it automatically. The selected file is never executed. The path is passed as a quoted argument, including when it contains spaces or Unicode characters.

The registration script also deploys to the fixed Local AppData path before writing the registry. Run it again with a newer `-ApplicationPath` to update or repair the menu.

**Remove menu** and the removal script delete only the three app-owned `CreateIntuneEpmRule` registry subtrees. They retain the managed EXE, `settings.json`, and the token cache. For complete removal of the managed executable, remove the menu, close the app, and delete only `%LOCALAPPDATA%\IntuneEpmRuleCreator\App` in Explorer. Account settings remain in the parent folder.

## What works offline

- SHA256 is calculated locally by the application.
- Available Windows version metadata and embedded publisher certificate details are shown where present.
- If `EpmCmdlets.dll` is installed, `Get-FileAttributes` is called locally through Windows PowerShell 5.1 and its hash is compared with the independent SHA256 result.
- Without EPM Tools, the basic file details remain available; hover **Analysis notes** to see missing-tool notices and other analysis warnings. Hover the **Signature** field for certificate details and signature-trust information. Nothing is silently installed.

These local operations do not require Microsoft Entra authentication, Graph permissions, a tenant, or an Internet connection. Certificate extraction alone does not establish that a signature is trusted.

With **Signature source → Upload a certificate file**, the **Use analyzed file certificate** radio option is selected automatically when the analyzed file contains a signing certificate. Choosing your own certificate file selects that uploaded certificate instead. You can switch back using the radio options; the certificate card under File information always shows the certificate extracted from the analyzed file. Changing the analyzed file clears any previously uploaded certificate.

To remove the registration:

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Unregister-ExplorerMenu.ps1
```

## Current limitations

Graph authentication and policy selection are optional for offline analysis. Uploading can append to a selected policy or create a new policy with its first rule after confirmation; see [Graph authentication](GraphAuthentication.md) and [Graph integration](GraphIntegration.md). Local analysis and validation do not create or modify policies.

Changing files immediately clears previous metadata and rule fields, including certificates and validation choices. Analyze the new file before validating or uploading. The current Entra account and selected target policy are retained. Explanations are in tooltips; errors and short statuses stay visible.

The development runner is Linux, so WPF and Explorer integration require a Windows smoke test: analyze two different files in sequence, add or repair the menu, move/delete the original downloaded EXE, launch from a supported file's right-click menu, update the managed copy, and remove the menu again. Automated tests cover command quoting, deployment, source deletion, updates, and failed replacement cleanup without modifying a real Windows registry. The Windows build also parses both Explorer scripts using Windows PowerShell.
