# Intune EPM Rule Creator

A Windows desktop tool for inspecting local files and preparing Microsoft Intune Endpoint Privilege Management elevation rules. Local file analysis works offline and does not require signing in to a tenant. Optional Microsoft Graph sign-in can list EPM policies, count their rules, browse reusable EPM certificate settings, and explicitly append a rule to a selected policy after confirmation.

> **Graph writes replace the selected Intune policy.** The app refreshes the policy and settings, validates the rule, and asks for confirmation before sending a PUT containing the complete settings collection. This follows the request captured from the Intune admin center; Microsoft Graph's public beta documentation currently describes PATCH instead. See [the verification record](docs/GraphIntegration.md) for current constraints.

## Current features

- Windows WPF desktop UI targeting .NET 10 and x64.
- File picker for `.exe`, `.msi`, and `.ps1` files.
- Independent SHA256 calculation and version resource metadata.
- Detection of Microsoft EPM Tools and invocation of `Get-FileAttributes` through Windows PowerShell 5.1 using JSON input, without placing the file path in PowerShell source code.
- Embedded publisher certificate extraction with subject, issuer, thumbprint, and validity dates. Certificate extraction is not an Authenticode trust check.
- Certificate-based rule selection between the analyzed file's extracted publisher certificate and a loaded reusable EPM certificate setting.
- Explicit Graph upload that appends to an existing EPM rule collection, preserves its other entries, rechecks the 100-rule limit, requests confirmation, and verifies the updated count.
- Local validation for rule name, file type, SHA256 format, selected policy requirement, 100-rule cap, user-writable path warnings, automatic elevation, and unrestricted child process warnings.
- Optional Graph browsing of EPM Elevation Rules policies and EPM reusable certificate settings (unrelated reusable settings are filtered out by setting definition ID).
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

Graph authentication is optional and uses MSAL as a native public client. The current development build accepts a client ID and tenant in the optional connection panel. It requests delegated `User.Read` to display the signed-in identity and `DeviceManagementConfiguration.ReadWrite.All` to list EPM policies/read and update their settings. Admin consent is required. No client secret is used.

The app saves the entered client ID and tenant ID in `%LOCALAPPDATA%\IntuneEpmRuleCreator\settings.json` so they are prefilled next time. This is a normal, unencrypted per-user settings file; these IDs are identifiers, not credentials. They are not written to the executable or Git repository. MSAL stores sign-in tokens separately in its cache under the same per-user app-data directory, protected with Windows DPAPI. Signing out removes the account's MSAL cache entries; it does not remove the saved IDs.

### Permissions for EPM policy browsing and writes

The app requests **delegated `DeviceManagementConfiguration.ReadWrite.All`**. Microsoft Graph documents it for configuration-policy settings updates and lists it as an accepted scope for policy, settings, and reusable policy setting reads. Admin consent is required. The current policy-level PUT follows a request captured from the Intune admin center; its permission requirements are not separately documented or live-validated yet.

- [Create a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-create?view=graph-rest-beta)
- [Update a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-update?view=graph-rest-beta)
- [List reusable policy settings](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementreusablepolicysetting-list?view=graph-rest-beta)
- [Permission reference: `DeviceManagementConfiguration.ReadWrite.All`](https://learn.microsoft.com/en-us/graph/permissions-reference#devicemanagementconfigurationreadwriteall)

Add `DeviceManagementConfiguration.ReadWrite.All` as a **delegated Microsoft Graph permission** to the Entra app registration and grant admin consent. Graph permission does not replace the Intune license or the signed-in user's Intune RBAC role and scope-tag access. The policy-setting API is currently Graph **beta**. See [optional Graph authentication](docs/GraphAuthentication.md) and [Graph integration research](docs/GraphIntegration.md).

The upload control only appends after explicit confirmation. It currently requires an existing Elevation Rules collection and an existing rule matching the selected identification/certificate source as a schema template; it does not create a policy or the first root setting. Deny-all child-process behavior is not yet supported by the verified payload mapping. The policy-level PUT still needs live validation in the test tenant; Graph beta and tenant-specific RBAC can reject a locally valid rule.

See [Graph integration research](docs/GraphIntegration.md) for the endpoints supplied by the brief, research links, and unresolved verification items.

## Test status

Automated tests cover local SHA256 rule validation, supported application file extensions, and the policy rule count boundary. They do not cover a real Windows EPM Tools installation, Explorer registration, Microsoft Entra authentication, or Graph. Those integration checks require Windows and, for Graph, a separately authorized test tenant.
