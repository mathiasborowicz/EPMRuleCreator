# Intune EPM Rule Creator

A Windows desktop tool for inspecting local files and preparing Microsoft Intune Endpoint Privilege Management elevation rules. Local file analysis works offline and does not require signing in to a tenant. Optional Microsoft Graph sign-in is available to verify the account and tenant; this build does not modify Intune policies.

> **Graph publishing is disabled.** The official Microsoft Learn pages needed to verify EPM setting identifiers and safe rule collection updates returned HTTP 403 from the build environment. The project does not guess Graph payloads or send policy changes. See [the verification record](docs/GraphIntegration.md).

## Current features

- Windows WPF desktop UI targeting .NET 10 and x64.
- File picker for `.exe`, `.msi`, and `.ps1` files.
- Independent SHA256 calculation and version resource metadata.
- Detection of Microsoft EPM Tools and invocation of `Get-FileAttributes` through Windows PowerShell 5.1 using JSON input, without placing the file path in PowerShell source code.
- Embedded publisher certificate extraction with subject, issuer, thumbprint, and validity dates. Certificate extraction is not an Authenticode trust check.
- Local validation for rule name, file type, SHA256 format, selected policy requirement, 100-rule cap, user-writable path warnings, automatic elevation, and unrestricted child process warnings.
- Per-user Explorer context menu registration and removal scripts.

### Offline file analysis

No Intune tenant, Graph connection, or network access is needed to inspect a file. The app calculates SHA256 locally, reads available Windows version metadata, and attempts to extract the embedded publisher certificate. Certificate extraction does not verify Authenticode trust.

Microsoft EPM Tools (`EpmCmdlets.dll`) are optional for the basic offline view. When they are installed, the app also calls `Get-FileAttributes` locally through Windows PowerShell 5.1. If they are absent, the app shows the locally available information and warns that EPM-specific attributes were not collected. It never installs the EPM agent or changes device configuration.

## Project structure

```text
src/IntuneEpmRuleCreator.Core/    Domain models, file analysis, validation
src/IntuneEpmRuleCreator.UI/      Windows WPF application and view model
tests/IntuneEpmRuleCreator.Tests/ Core unit tests
scripts/                          Per-user Explorer menu registration
docs/                             Graph verification and installation notes
```

## Build, test, and install

See [Installation](docs/Installation.md) for obtaining the `work` branch in `C:\Git\EPMRuleCreator`, installing the .NET SDK, building the self-contained app, and registering the Explorer menu.

## Graph, authentication, and security

Graph authentication is optional and uses MSAL as a native public client. The current development build accepts a client ID and tenant in the optional connection panel. It requests delegated `User.Read` only to display the signed-in identity. No client secret is used. This scope does not grant Intune policy access, and no policy operations are implemented.

The app saves the entered client ID and tenant ID in `%LOCALAPPDATA%\IntuneEpmRuleCreator\settings.json` so they are prefilled next time. This is a normal, unencrypted per-user settings file; these IDs are identifiers, not credentials. They are not written to the executable or Git repository. MSAL stores sign-in tokens separately in its cache under the same per-user app-data directory, protected with Windows DPAPI. Signing out removes the account's MSAL cache entries; it does not remove the saved IDs.

### Permission needed for future EPM rule writes

Microsoft Graph documents the delegated permission **`DeviceManagementConfiguration.ReadWrite.All`** for creating and updating configuration-policy settings, the Graph settings that hold policy values. It also permits the policy and settings read operations needed by the app, so a separate `DeviceManagementConfiguration.Read.All` scope is not required. The permission reference marks admin consent as required. For these settings endpoints, `DeviceManagementEndpointSecurity.ReadWrite.All` is not needed.

- [Create a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-create?view=graph-rest-beta)
- [Update a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-update?view=graph-rest-beta)
- [Permission reference: `DeviceManagementConfiguration.ReadWrite.All`](https://learn.microsoft.com/en-us/graph/permissions-reference#devicemanagementconfigurationreadwriteall)

To enable future writes, add this as a **delegated Microsoft Graph permission** to the Entra app registration and grant admin consent. The app must also request it during sign-in; currently it requests only `User.Read`, so it cannot write Intune settings yet. Graph permission does not replace the Intune license or the signed-in user's Intune RBAC role and scope-tag access. The policy-setting API is currently Graph **beta**. See [optional Graph authentication](docs/GraphAuthentication.md) and [Graph integration research](docs/GraphIntegration.md).

No policy changes occur in this build. The UI does not provide a create/publish control. Never interpret local rule validation as proof that a rule is accepted by Intune.

See [Graph integration research](docs/GraphIntegration.md) for the endpoints supplied by the brief, research links, and unresolved verification items.

## Test status

Automated tests cover local SHA256 rule validation, supported application file extensions, and the policy rule count boundary. They do not cover a real Windows EPM Tools installation, Explorer registration, Microsoft Entra authentication, or Graph. Those integration checks require Windows and, for Graph, a separately authorized test tenant.
