# Intune EPM Rule Creator

A Windows desktop tool for inspecting local files and preparing Microsoft Intune Endpoint Privilege Management elevation rules. Local file analysis works offline and does not require signing in to a tenant. The current build does not connect to Graph or modify Intune policies.

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

No MSAL client ID, Graph scope, client secret, or tenant setting is requested in the current version because no Graph calls are implemented. The permission names in the project brief are candidates only. They must be matched to verified API operations, least privilege, and Intune RBAC before authentication or policy writes are added.

No policy changes occur in this build. The UI does not provide a create/publish control. Never interpret local rule validation as proof that a rule is accepted by Intune.

See [Graph integration research](docs/GraphIntegration.md) for the endpoints supplied by the brief, research links, and unresolved verification items.

## Test status

Automated tests cover local SHA256 rule validation, supported application file extensions, and the policy rule count boundary. They do not cover a real Windows EPM Tools installation, Explorer registration, Microsoft Entra authentication, or Graph. Those integration checks require Windows and, for Graph, a separately authorized test tenant.
