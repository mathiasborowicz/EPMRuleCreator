# Installation and Explorer integration

## Requirements

- Windows 11 x64.
- Git for Windows to obtain the source branch.
- .NET 10 SDK to build. The published application is self-contained and does not need a separate .NET runtime.
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
dotnet publish .\src\IntuneEpmRuleCreator.UI\IntuneEpmRuleCreator.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish\win-x64
```

The executable is `publish\win-x64\IntuneEpmRuleCreator.exe`. Run it directly to select a file in the app. No administrator rights, tenant connection, or network are needed for local file analysis.

## Register the Explorer menu

From a normal, non-elevated PowerShell session in the repository root, register the current user's menu:

```powershell
$appPath = Join-Path $PWD 'publish\win-x64\IntuneEpmRuleCreator.exe'
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Register-ExplorerMenu.ps1 -ApplicationPath $appPath
```

The registration is per-user under `HKEY_CURRENT_USER` and does not require administrator privileges. Windows 11 shows this classic registry menu item under **Show more options**; it is not in the primary modern context menu. The menu is registered for `.exe`, `.msi`, and `.ps1` files. Right-click one of those files, choose **Show more options**, then **Create Intune EPM Rule**. The application starts with that path and analyzes it automatically. The selected file is never executed. The path is passed as a quoted argument, including when it contains spaces or Unicode characters.

Keep the published executable at the same path after registration. If you move it, run the registration script again with the new `-ApplicationPath`.

## What works offline

- SHA256 is calculated locally by the application.
- Available Windows version metadata and embedded publisher certificate details are shown where present.
- If `EpmCmdlets.dll` is installed, `Get-FileAttributes` is called locally through Windows PowerShell 5.1 and its hash is compared with the independent SHA256 result.
- Without EPM Tools, the basic file details remain available and the app warns that EPM-specific attributes are missing. Nothing is silently installed.

These local operations do not require Microsoft Entra authentication, Graph permissions, a tenant, or an Internet connection. Certificate extraction alone does not establish that a signature is trusted.

To remove the registration:

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Unregister-ExplorerMenu.ps1
```

## Current limitations

Graph authentication and Intune policy operations remain disabled until the EPM setting model is verified; see [Graph integration](GraphIntegration.md). Local analysis and validation do not create or modify policies. The solution and self-contained Windows x64 publish build successfully, and the six core unit tests pass. The development runner is Linux, so the WPF app, PowerShell integration, and Explorer registration still need an on-device Windows smoke test.
