# Intune EPM Rule Creator

[![Windows 11 x64](https://img.shields.io/badge/Windows-11_x64-0078D4?logo=windows&logoColor=white)](docs/Installation.md)
[![Offline first](https://img.shields.io/badge/Offline-first-2ea44f)](#-get-started)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**From a file to an Intune EPM rule, without the busywork.**

Inspect file details, check signing certificates, and prepare Microsoft Intune Endpoint Privilege Management rules in one Windows app. File analysis works offline. Connect to Intune only when you want to upload a rule.

### [⬇ Download the latest EXE](https://github.com/mathiasborowicz/EPMRuleCreator/releases/latest/download/IntuneEpmRuleCreator.exe)

Just the EXE. No compiling, no separate .NET installation, no account needed to get started.

## 🚀 Get started

1. Download and open **IntuneEpmRuleCreator.exe** on Windows 11 x64.
2. Choose an `.exe`, `.msi`, or `.ps1` file and click **Analyze file**.
3. Review its details and prepare your elevation rule. That's it for offline use!

Want to send the rule to Intune? Sign in using the account icon at the top right, select an **Elevation Rules policy**, and confirm the upload. Choose **+ Add new** to create a policy with its first rule instead.

*📸 Screenshot coming soon.*

<!-- Screenshot slot: replace the line above with the supplied app screenshot when available. Do not add a broken image link. -->

## 🔎 What you can do

- **See what's in a file:** SHA256, version, product details, and its signing certificate, where available.
- **Choose how to identify it:** file hash, the extracted signing certificate, your own certificate file, or an Intune reusable certificate setting.
- **Prepare elevation rules:** configure elevation type, validation requirements, child processes, and arguments.
- **Upload when you're ready:** add a rule to an existing policy or create a new policy with its first rule.
- **Start from a right-click:** use **Add menu** to enable Explorer integration. **Remove menu** undoes it; **↻** updates the installed copy.

On Windows 11, the Explorer entry appears under **Show more options**. You can move or delete the original download after adding the menu. [How it works](docs/Installation.md#register-the-explorer-menu).

## 🔐 Intune connection is optional

No tenant, Entra app registration, or EPM Tools installation is needed for basic file analysis. If Microsoft EPM Tools are already installed, the app also reads their additional file attributes. It never installs an agent or runs the file you analyze.

<details>
<summary><strong>Connecting to Intune: permissions and local settings</strong></summary>

For Intune uploads, configure your own Entra public-client app registration with the `http://localhost` redirect URI. No client secret is needed.

Required delegated Graph permissions:

- `User.Read`
- `DeviceManagementConfiguration.ReadWrite.All` with admin consent

Your account also needs the appropriate Intune license, RBAC permissions, and scope-tag access. Follow the [sign-in setup guide](docs/GraphAuthentication.md).

The app remembers your client ID and tenant in `%LOCALAPPDATA%\IntuneEpmRuleCreator\settings.json`. This file is unencrypted; the IDs are identifiers, not secrets, and are **not embedded in the EXE or repository**. Sign-in tokens are stored separately using Windows DPAPI. Signing out clears the account's token-cache entries, not the saved IDs.

</details>

> Test uploads in a test tenant first. Updates require confirmation and send the full policy settings collection, preserving other rules. Graph beta has [limitations](docs/GraphIntegration.md). New policies need assignments in Intune. Certificate extraction is not a signature-trust check.

## 📚 Need more detail?

- [Installation, updates, and Explorer menu](docs/Installation.md)
- [Entra sign-in and Graph permissions](docs/GraphAuthentication.md)
- [Graph behavior and current limitations](docs/GraphIntegration.md)

Building it yourself? The [installation guide](docs/Installation.md#build-and-publish) covers the .NET 10 SDK and build commands. Merges to `main` automatically publish a tested, versioned EXE; development builds are available in Actions.

## 🤝 Feedback welcome

Found a bug or have an idea? [Open an issue](https://github.com/mathiasborowicz/EPMRuleCreator/issues) or send a pull request. Include your app version and steps to reproduce. Please redact tokens and personal or tenant-specific details.

## License

[GNU GPLv3](LICENSE), like [AppPermissionInfo](https://github.com/mathiasborowicz/AppPermissionInfo). Commercial use is allowed; redistributed changes must remain under GPLv3 with the corresponding source available. No warranty. Third-party dependencies retain their own licenses.

Copyright (c) 2026 Mathias Borowicz.
