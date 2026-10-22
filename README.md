<p><img src="docs/assets/epm-rule-creator.png" alt="EPMRuleCreator logo" width="96" height="96"></p>

# EPMRuleCreator

**Inspect a file. Understand its signature. Create an Intune EPM rule.**

[![Windows 11 x64](https://img.shields.io/badge/Windows-11_x64-0078D4?logo=windows&logoColor=white)](docs/Installation.md)
[![Offline first](https://img.shields.io/badge/Offline-first-2ea44f)](docs/UserGuide.md)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

EPMRuleCreator is a Windows desktop tool for people who manage Microsoft Intune Endpoint Privilege Management (EPM). Select an application, inspect its file details and signing certificate, and prepare an elevation rule without copying everything by hand.

**Just need the file information? Stop there.** File analysis and certificate export work locally, without signing in. Connecting to Intune is entirely optional.

### [⬇ Download EPMRuleCreator](https://github.com/mathiasborowicz/EPMRuleCreator/releases/latest/download/EPMRuleCreator-win-x64.zip)

Extract the ZIP and open `EPMRuleCreator.exe`. No compiling, no Git, no separate .NET installation.

> 📸 **Screenshot coming soon:** the main window with an analyzed file, its signing certificate chain, and a prepared rule.
<!-- SCREENSHOT: overview. Replacement instructions and suggested filenames: docs/Screenshots.md. -->

## 🚀 From file to rule

1. **Choose how to run it.** Choose **Use portable** to keep the EXE in your folder, or **Install for this user** for a fixed AppData location and Start menu shortcut.
2. **Analyze a file.** Choose an `.exe`, `.msi`, or `.ps1` file and click **Analyze file**. Review SHA256, available version information, and signing certificates. The selected file is never executed.
3. **Prepare your rule.** Review the prefilled fields, choose file-hash or certificate identification, and set the elevation behavior. **Validate rule** checks the local inputs.
4. **Upload only if you want to.** Sign in through the account icon, select an **Elevation Rules policy**, or choose **+ Add new**. Review the rule, then click the upload button. Results appear in **Logs**.

New to the workflow? The [step-by-step walkthrough](docs/UserGuide.md) explains each step and what the choices mean.

## ✨ What you can do

- **Inspect locally:** see SHA256, product/version metadata, and the available signing certificate chain.
- **Export public certificates:** save each chain certificate as its own `.cer`, or export a P7B/PEM bundle.
- **Choose the right match:** use a file hash, the file's signing certificate, your own certificate file, or an EPM reusable certificate setting.
- **Prepare EPM rules:** configure elevation type, user validation, child processes, and argument restrictions.
- **Write to Intune:** append a rule to an existing policy or create a policy with its first rule.
- **Work from Explorer:** **Add menu** registers the current EXE. **Remove menu** removes the entry; **↻** repairs its path. No hidden EXE copy is created.
- **Keep it comfortable:** light/dark themes inspired by Zero Trust Stories, optional startup update checks, and an **Update** button that installs a verified release and restarts the app.

Portable means the EXE stays where you put it. If you move it, repair its context-menu entry. Installed mode uses one fixed EXE in AppData; the original download can be deleted after installation. [Installation and updates](docs/Installation.md).

## 🔐 Local first. Intune when you need it.

You do **not** need a tenant, an Entra app registration, or EPM Tools for basic file analysis. **Use EpmCmdlets** is optional, off by default, and disabled when its DLL is missing. The tool does not install an EPM agent, run the analyzed file, or elevate applications itself.

For Intune, bring your own Entra public-client app registration with the `http://localhost` redirect URI. Required delegated Graph permissions are `User.Read` and `DeviceManagementConfiguration.ReadWrite.All`, with admin consent for the latter. Your account also needs the appropriate Intune licensing, RBAC, and scope-tag access. [Set up sign-in](docs/GraphAuthentication.md).

<details>
<summary>What is saved on my computer?</summary>

`%LOCALAPPDATA%\EPMRuleCreator\settings.json` stores the client ID, tenant, theme preference, and startup-update preference. It is unencrypted: these IDs are identifiers, not credentials. No client/tenant ID is embedded in the EXE or repository, and no client secret is needed.

Tokens stay in RAM for the app session; no new `msal.cache` is written. Closing the app ends its session, though the browser may still provide Microsoft SSO. Old cache files are not automatically deleted. [Token handling and cleanup](docs/GraphAuthentication.md#runtime-and-token-handling).

Portable mode creates an empty `EPMRuleCreator.portable` marker beside the EXE to remember your choice; preferences still use AppData. Logs are session-only and are not automatically saved. Optional update checks contact public GitHub releases, not Intune, and can be disabled in **Updates**.

</details>

> **Before your first upload:** test in a test tenant. Upload writes immediately, without an extra confirmation popup. New policies have no assignments; configure those in Intune. Extracting a certificate does not prove a signature is trusted. [Graph behavior and limitations](docs/GraphIntegration.md).

## 📚 Find your next step

- [Using the tool: file analysis, certificates, and rules](docs/UserGuide.md)
- [Download, portable/install modes, updates, and Explorer integration](docs/Installation.md)
- [Entra registration, Graph permissions, and sign-in](docs/GraphAuthentication.md)
- [Technical Graph behavior and limitations](docs/GraphIntegration.md)
- [Screenshot checklist for contributors](docs/Screenshots.md)

## 🤝 Made for the community

Found a bug or have an idea? [Open an issue](https://github.com/mathiasborowicz/EPMRuleCreator/issues) or send a pull request. Include the app version, what you did, and the relevant error from **Logs**. Redact personal information, tenant details, and tokens.

Want to build it yourself? See [Build and publish](docs/Installation.md#build-and-publish). Successful merges to `main` publish a versioned release ZIP automatically.

## License

Independent community project, not affiliated with or endorsed by Microsoft. Microsoft product names describe the services this tool works with.

[GNU GPLv3](LICENSE). Commercial use is allowed; redistributed changes must remain under GPLv3 with the corresponding source available. No warranty. Third-party dependencies retain their own licenses.

Copyright (c) 2026 Mathias Borowicz.
