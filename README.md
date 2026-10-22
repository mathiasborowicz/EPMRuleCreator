<p><img src="docs/assets/epm-rule-creator.png" alt="EPMRuleCreator logo" width="96" height="96"></p>

# EPMRuleCreator

**From a file to an Intune EPM rule.**

[![Windows 11 x64](https://img.shields.io/badge/Windows-11_x64-0078D4?logo=windows&logoColor=white)](docs/Installation.md)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

Check file details and signing certificates, prepare an elevation rule, and upload it to Microsoft Intune when you're ready. File analysis works locally, without signing in.

### [⬇ Download EPMRuleCreator](https://github.com/mathiasborowicz/EPMRuleCreator/releases/latest/download/EPMRuleCreator-win-x64.zip)

Extract the ZIP and open `EPMRuleCreator.exe`.

> 📸 Screenshot coming soon.
<!-- SCREENSHOT: overview. See docs/Screenshots.md. -->

## 🚀 Get started

1. Choose **Use portable** or **Install for this user**.
2. Select a file and click **Analyze file**. Review the file details, certificates, and prefilled rule.
3. Want to upload? Sign in, select a policy or **+ Add new**, and click the upload button.

That's it. You can also use the tool just to inspect files or export certificates.

## ✨ Handy extras

- Export certificates individually or as a chain bundle.
- Choose file-hash or certificate-based rules.
- Add an Explorer right-click entry during installation.
- Switch between light and dark themes.
- Check for updates, or turn automatic checks off.

> Upload writes immediately. Test in a test tenant first. New policies need assignments in Intune.

## 📚 Need a hand?

- [Using the tool](docs/UserGuide.md)
- [Installation and updates](docs/Installation.md)
- [Connecting to Intune](docs/GraphAuthentication.md)

<details>
<summary>Intune permissions and local settings</summary>

For uploads, use your own Entra app registration with delegated `User.Read` and `DeviceManagementConfiguration.ReadWrite.All` permissions. The latter requires admin consent. [Sign-in setup](docs/GraphAuthentication.md).

Your client ID, tenant, and preferences are saved in `%LOCALAPPDATA%\EPMRuleCreator\settings.json`. This file is unencrypted and contains no tokens. Tokens stay in RAM; no new `msal.cache` is written. No client or tenant ID is embedded in the app. [Details](docs/GraphAuthentication.md#runtime-and-token-handling).

</details>

## 🤝 Feedback

[Report a bug or suggest an idea](https://github.com/mathiasborowicz/EPMRuleCreator/issues). Include your app version and the relevant error from **Logs**, with personal details removed.

## License

[GNU GPLv3](LICENSE). Commercial use is allowed; redistributed changes must remain under GPLv3 with the corresponding source available. No warranty. Third-party dependencies retain their own licenses.

Independent community project, not affiliated with or endorsed by Microsoft.

Copyright (c) 2026 Mathias Borowicz.
