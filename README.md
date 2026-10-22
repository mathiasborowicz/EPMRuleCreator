# Intune EPM Rule Creator

A Windows desktop tool for inspecting local files and preparing Microsoft Intune Endpoint Privilege Management elevation rules. Local file analysis works offline and does not require signing in to a tenant. Optional Microsoft Graph sign-in can list EPM policies, count their rules, browse reusable EPM certificate settings, append a rule to a selected policy, or create a new policy with its first rule after confirmation.

> **Uploads to existing policies replace the selected Intune policy.** The app refreshes the policy and settings, validates the rule, and asks for confirmation before sending a PUT containing the complete settings collection. This follows the request captured from the Intune admin center; Microsoft's public beta documentation currently describes PATCH instead. Creating a new policy uses the documented POST route with the first rule included. See [the verification record](docs/GraphIntegration.md) for current constraints.

## Current features

- Windows WPF desktop UI targeting .NET 10 and x64.
- File picker for `.exe`, `.msi`, and `.ps1` files.
- Independent SHA256 calculation and version resource metadata.
- Detection of Microsoft EPM Tools and invocation of `Get-FileAttributes` through Windows PowerShell 5.1 using JSON input, without placing the file path in PowerShell source code.
- Embedded publisher certificate extraction with subject, issuer, thumbprint, and validity dates. Certificate extraction is not an Authenticode trust check.
- Certificate-based rule selection between the analyzed file's extracted publisher certificate and a reusable EPM certificate setting. Selecting **Use a certificate file in reusable settings** loads EPM certificates automatically into the rule's **Certificate** dropdown after sign-in; there is no separate reusable-settings section. Loading errors and empty results appear directly below that dropdown; open it again to retry a failed load.
- Explicit Graph upload that appends to an existing EPM rule collection, preserves its other entries, rechecks the 100-rule limit, requests confirmation, and verifies the updated count.
- Local validation for rule name, conditional file-name/SHA256 requirements, optional three- or four-part numeric minimum file version (1–30 characters when set), selected policy requirement, 100-rule cap, user-writable path warnings, automatic elevation, and unrestricted child process warnings.
- Optional Graph browsing of EPM Elevation Rules policies and EPM reusable certificate settings (unrelated reusable settings are filtered out by setting definition ID).
- Elevation Rules policies appear directly in the rule editor's dropdown and load automatically after sign-in. Selecting a policy reads its rule count automatically. There are no separate policy/reusable-settings panels or manual load/count buttons; reopen a dropdown to retry a failed request.
- **+ Add new** in the policy dropdown opens a policy name and optional description, including when no policies exist. **Create policy and upload rule** validates the form and asks for confirmation before creating the policy with its first rule in one POST. The new policy uses the default scope tag (`0`) and has no assignments; configure those in Intune. No existing rule is needed for this creation flow.
- Per-user Explorer context menu registration and removal scripts.

### Offline file analysis

No Intune tenant, Graph connection, or network access is needed to inspect a file. The app calculates SHA256 locally, reads available Windows version metadata, and attempts to extract the embedded publisher certificate. Certificate extraction does not verify Authenticode trust.

Without a certificate, file name and SHA256 are required to identify the file. With a publisher or reusable certificate selected, file name and hash are optional constraints and are omitted from the Graph payload if blank.

The elevation rule's **Minimum file version** field is prefilled from the analyzed file's version metadata when available and can be edited. It is optional. When supplied, Intune validates it against the format `number.number.number.number` and limits it to 30 characters; clear it if no matching version value is available.

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

Graph authentication is optional and uses MSAL as a native public client. Click the circular account button with the person icon at the top right to enter the Application (client) ID and tenant, then choose **Sign in**. After signing in, the button shows the account's initials and a green connection indicator; click it again to see the account or sign out. The panel closes when you click outside it or press Escape. EPM policies load automatically after sign-in. The app requests delegated `User.Read` to display the signed-in identity and `DeviceManagementConfiguration.ReadWrite.All` to list EPM policies/read and update their settings. Admin consent is required. No client secret is used.

The app saves the entered client ID and tenant ID in `%LOCALAPPDATA%\IntuneEpmRuleCreator\settings.json` so they are prefilled next time. This is a normal, unencrypted per-user settings file; these IDs are identifiers, not credentials. They are not written to the executable or Git repository. MSAL stores sign-in tokens separately in its cache under the same per-user app-data directory, protected with Windows DPAPI. Signing out removes the account's MSAL cache entries; it does not remove the saved IDs.

### Permissions for EPM policy browsing and writes

The app requests **delegated `DeviceManagementConfiguration.ReadWrite.All`**. Microsoft Graph documents it for configuration-policy settings updates and lists it as an accepted scope for policy, settings, and reusable policy setting reads. Admin consent is required. The current policy-level PUT follows a request captured from the Intune admin center; its permission requirements are not separately documented or live-validated yet.

- [Create a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-create?view=graph-rest-beta)
- [Update a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-update?view=graph-rest-beta)
- [List reusable policy settings](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementreusablepolicysetting-list?view=graph-rest-beta)
- [Permission reference: `DeviceManagementConfiguration.ReadWrite.All`](https://learn.microsoft.com/en-us/graph/permissions-reference#devicemanagementconfigurationreadwriteall)

Add `DeviceManagementConfiguration.ReadWrite.All` as a **delegated Microsoft Graph permission** to the Entra app registration and grant admin consent. Graph permission does not replace the Intune license or the signed-in user's Intune RBAC role and scope-tag access. The policy-setting API is currently Graph **beta**. See [optional Graph authentication](docs/GraphAuthentication.md) and [Graph integration research](docs/GraphIntegration.md).

Writes require explicit confirmation. Appending to an existing policy currently requires an Elevation Rules collection with an existing rule; its elevation type need not match. For certificate rules, the existing-policy path uses a matching certificate-source schema from that policy. Creating a new policy uses a separate payload builder and requires no existing policy or rule. Certificate rules expose the `Publisher` and `Certificate authority` types. User-confirmed rules expose optional Windows authentication and business justification validations; current-user rules require Windows authentication and optionally support business justification. Child-process behavior supports allow, require a matching rule, deny, and not configured. Empty optional text fields and unselected optional validation collections are omitted. The UI requires exactly four numeric version parts. Analyzed three-part versions such as `1.1.0` are prefilled as `1.1.0.0`; the field remains optional. The existing-policy PUT has been confirmed in the test tenant. The new-policy POST has automated payload and HTTP tests but still requires a live test in the tenant.

See [Graph integration research](docs/GraphIntegration.md) for the endpoints supplied by the brief, research links, and unresolved verification items.

## Test status

Automated tests cover rule validation, supported file extensions, the rule count boundary, Graph payload construction, and mocked new-policy HTTP requests and error handling. They do not authenticate against Microsoft Entra or make live Graph writes. Windows EPM Tools, Explorer registration, sign-in, and new-policy creation require integration testing on Windows with an authorized test tenant.
