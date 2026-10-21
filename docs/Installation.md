# Installation and Explorer integration

## Requirements

- Windows 11 x64.
- .NET 10 SDK to build, or the self-contained publish output produced below.
- Microsoft Intune Endpoint Privilege Management Tools installed separately for `Get-FileAttributes`. The application does not install the EPM agent or change its configuration.

## Build and publish

Run from the repository root on Windows:

```powershell
dotnet test .\IntuneEpmRuleCreator.sln
dotnet publish .\src\IntuneEpmRuleCreator.UI\IntuneEpmRuleCreator.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

Copy the publish directory to a per-user location, for example `%LOCALAPPDATA%\Programs\IntuneEpmRuleCreator`. The app does not require elevation for ordinary local file analysis.

## Register the Explorer menu

From a normal PowerShell session, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Register-ExplorerMenu.ps1 -ApplicationPath "$env:LOCALAPPDATA\Programs\IntuneEpmRuleCreator\IntuneEpmRuleCreator.exe"
```

The registration is per-user under `HKEY_CURRENT_USER` and does not require administrator privileges. Windows 11 shows this classic registry menu item under **Show more options**. It is not a packaged modern context menu provider. The menu is registered for `.exe`, `.msi`, and `.ps1` files. Selecting the entry launches the application with the selected path; it never launches that file for execution.

To remove the registration:

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Unregister-ExplorerMenu.ps1
```

## Current limitations

The app currently supports local analysis and local validation only. Graph authentication and policy operations remain disabled until the EPM setting model is verified; see [Graph integration](GraphIntegration.md). Signature trust is not established by extracting an embedded certificate. The current runner was Linux and had no .NET SDK, so Windows build, PowerShell, EPM Tools, and Explorer checks remain to be run on Windows.
