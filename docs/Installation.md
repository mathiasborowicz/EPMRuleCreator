# Get EPMRuleCreator running

For normal use, you only need Windows 11 x64 and the release ZIP. Git and the .NET SDK are for contributors, not a requirement to run the tool.

## Download and start

1. Download [EPMRuleCreator-win-x64.zip](https://github.com/mathiasborowicz/EPMRuleCreator/releases/latest/download/EPMRuleCreator-win-x64.zip).
2. Extract the ZIP. It contains the standalone `EPMRuleCreator.exe` and `LICENSE`, not the source project.
3. Open the EXE and choose portable or installed mode.
4. Choose a file and click **Analyze file**. No sign-in or administrator rights are needed for basic analysis.

If Windows marks a trusted manual download as blocked, open its **Properties** and select **Unblock**, if available. ZIP packaging does not bypass Defender, SmartScreen, or ASR. Follow your organization's security policy; the app does not disable those protections.

> 📸 **Screenshot placeholder: first-start mode selection.** Show the portable and per-user installation choices.
<!-- SCREENSHOT: startup-modes. See Screenshots.md. -->

## Portable or installed

### Use portable

The EXE runs in the folder where you extracted it. There is no additional EXE copy in AppData. An empty `EPMRuleCreator.portable` marker next to it remembers your choice; keep that marker when moving the app. On read-only media, the choice applies to this session only.

Updates replace this same EXE. If you move it after adding the Explorer menu, open it at the new location and click **↻** to repair the menu.

Portable refers to the executable, not all user data: preferences still use `%LOCALAPPDATA%\EPMRuleCreator\settings.json`, and tokens remain in RAM.

### Install for this user

This explicitly copies the EXE to `%LOCALAPPDATA%\EPMRuleCreator\App\EPMRuleCreator.exe`, creates a Start menu shortcut, and opens that installation. No admin rights are required. You can then delete the original downloaded EXE; it is not automatically removed.

The shortcut, context menu, and updater use the same installed EXE. If an installation already exists, the startup choice becomes **Open installed app**. It opens that copy without overwriting it with a potentially older download. Use **Updates** in the installed app for newer versions.

From portable mode, **Updates > Install for this user** (or **Open installed app**) switches to the fixed installation. Save rule edits first. The **Updates** panel shows the current mode; hover it to see the running EXE path.

Old application copies are not automatically deleted. To display the first-start choices again for a portable folder, close the app and remove only its `EPMRuleCreator.portable` marker.

## Updates

The app checks public GitHub releases at startup by default. Open **Updates** and uncheck **Check for updates at startup** to disable that request. **Check now** works on demand. Neither operation requires Entra sign-in or sends analyzed-file details, tenant IDs, or Graph tokens to GitHub. Offline or rate-limited checks do not block file analysis.

When a newer stable release is available, **Update** downloads the release ZIP, verifies its GitHub SHA256 digest and the EXE's product/version, then replaces the running EXE and restarts. Save your rule edits first. The folder must be writable. An installed app stays installed; a portable app stays portable.

The old executable remains next to it as `EPMRuleCreator.exe.previous`. If replacement or restart fails, the helper attempts to restore it. The updater removes an Internet zone marker only from the verified replacement using `Unblock-File` in Windows PowerShell 5.1. It does not change execution policy, Defender, or ASR. If that helper is blocked by your organization, use a manual update.

Without a usable GitHub checksum, automatic installation is unavailable; **Open release page** provides the manual download instead. To update manually, close the app, extract the new EXE into its current folder, and keep your portable marker if present. This is also the recovery path for older versions whose updater fails.

> 📸 **Screenshot placeholder: Updates panel.** Show the installed version, mode, startup-check toggle, and available-update action.
<!-- SCREENSHOT: updates. See Screenshots.md. -->

## Register the Explorer menu

Click **Add menu** in the header. The app registers the exact running EXE for `.exe`, `.msi`, and `.ps1` files. This does not install or copy another executable.

On Windows 11, right-click a file, choose **Show more options**, then **Create EPM Rule**. The app opens with that file and analyzes it. The selected file is never executed, and its path is passed as a quoted argument.

- **Remove menu** removes the app-owned menu entries.
- **↻** points the menu to the current EXE, for example after moving a portable copy. It does not update an application binary.

Registration is per-user under `HKEY_CURRENT_USER`; no admin rights are needed. Existing registrations are repointed when you select a mode. Legacy `CreateIntuneEpmRule` entries are removed only after new entries are registered; removal recognizes both names.

If you use the source repository, the PowerShell alternative also registers the exact supplied path without deploying a copy:

```powershell
$appPath = Join-Path $PWD 'publish\win-x64\EPMRuleCreator.exe'
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Register-ExplorerMenu.ps1 -ApplicationPath $appPath
```

Remove it with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Unregister-ExplorerMenu.ps1
```

## Local preferences and appearance

The app follows your Windows app theme at startup until you use its **Dark** switch. That manual choice is saved locally. Live Windows theme changes while the app is open are not tracked. The palette takes inspiration from the supplied Zero Trust Stories screenshot: dark navy/graphite cards, fine borders, and pale blue accents, with a corresponding light theme. These are screenshot-based choices, not verified website CSS or fonts.

`%LOCALAPPDATA%\EPMRuleCreator\settings.json` contains these preferences:

- `ClientId` and `TenantId`: your optional sign-in identifiers, not secrets.
- `IsDarkTheme`: `true` for dark, `false` for light; missing or `null` follows Windows at startup.
- `CheckForUpdatesAtStartup`: `true` by default, or `false` to opt out.

The file is unencrypted and contains no tokens. Change preferences in the app where possible. If editing the file, close the app first and preserve the other fields. See [token handling](GraphAuthentication.md#runtime-and-token-handling) for RAM-only authentication and older cache files.

**Coming from the old application name?** If the new settings path is missing, the app copies only `settings.json` from `%LOCALAPPDATA%\IntuneEpmRuleCreator`. It keeps the original, never overwrites existing new settings, and does not copy old token caches. An EXE already at the current fixed installation path is recognized as installed. Old EXE copies are not deleted; **↻** repairs the menu to the EXE you actually run.

## Remove the app

First click **Remove menu** and close the app. For portable mode, remove the portable EXE and marker. For installed mode, remove the EPMRuleCreator Start menu shortcut and only `%LOCALAPPDATA%\EPMRuleCreator\App` in Explorer. Preferences remain in the parent folder unless you remove them separately. Menu removal never deletes your EXE or settings.

## Build and publish

Only needed if you want to contribute or compile your own build. Install Git for Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), not just the runtime. With winget:

```powershell
winget install --exact --id Microsoft.DotNet.SDK.10
```

Reopen PowerShell and check `dotnet --list-sdks`. Clone into a new or empty folder, preserving any existing project files:

```powershell
git clone https://github.com/mathiasborowicz/EPMRuleCreator.git C:\Git\EPMRuleCreator
Set-Location C:\Git\EPMRuleCreator
dotnet test .\EPMRuleCreator.sln
dotnet publish .\src\EPMRuleCreator.UI\EPMRuleCreator.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true -o .\publish\win-x64
```

The EXE is `publish\win-x64\EPMRuleCreator.exe`. The development branch is `work`. Actions builds on `work` and `main` provide a compressed, self-contained Windows x64 EXE artifact retained for 30 days. Native WPF components are bundled and extracted by .NET at startup.

Successful pushes/merges to `main` publish a versioned GitHub release ZIP with the EXE and license. Tags use `vX.Y.Z`, with the same version embedded in the EXE. The release workflow increments the latest patch version; increasing the UI project's baseline starts a new version line. Retrying a tagged commit reuses its release. Failed builds and pull-request checks do not publish releases.

The Linux development runner cannot exercise WPF, Explorer, shortcuts, or the real updater end to end. Windows smoke tests should cover both modes, file switching, optional EpmCmdlets failure/fallback, certificate exports, theme changes, menu repair/removal, and update/restart. Automated tests and Windows workflow checks complement, but do not replace, those checks.
