# Intune EPM Rule Creator

A Windows desktop foundation for preparing Microsoft Intune Endpoint Privilege Management elevation rules from local files. The current build implements file analysis and local rule validation. It does not yet connect to a tenant or modify Intune policies.

> **Graph publishing is disabled.** The official Microsoft Learn pages needed to verify EPM setting identifiers and safe rule collection updates returned HTTP 403 from the build environment. The project does not guess Graph payloads or send policy changes. See [the verification record](docs/GraphIntegration.md).

## Current features

- Windows WPF desktop UI targeting .NET 10 and x64.
- File picker for `.exe`, `.msi`, and `.ps1` files.
- Independent SHA256 calculation and version resource metadata.
- Detection of Microsoft EPM Tools and invocation of `Get-FileAttributes` through Windows PowerShell 5.1 using JSON input, without placing the file path in PowerShell source code.
- Embedded publisher certificate extraction with subject, issuer, thumbprint, and validity dates. Certificate extraction is not an Authenticode trust check.
- Local validation for rule name, file type, SHA256 format, selected policy requirement, 100-rule cap, user-writable path warnings, automatic elevation, and unrestricted child process warnings.
- Per-user Explorer context menu registration and removal scripts.

Missing EPM Tools do not trigger agent installation or configuration changes. In that case the app still shows independently calculated SHA256 and Windows file metadata, with a visible warning that EPM attributes were not obtained.

## Project structure

```text
src/IntuneEpmRuleCreator.Core/    Domain models, file analysis, validation
src/IntuneEpmRuleCreator.UI/      Windows WPF application and view model
tests/IntuneEpmRuleCreator.Tests/ Core unit tests
scripts/                          Per-user Explorer menu registration
docs/                             Graph verification and installation notes
```

## Build, test, and install

See [Installation](docs/Installation.md) for Windows prerequisites, `dotnet test`, self-contained x64 publish, and Explorer integration steps.

## Graph, authentication, and security

No MSAL client ID, Graph scope, client secret, or tenant setting is requested in the current version because no Graph calls are implemented. The permission names in the project brief are candidates only. They must be matched to verified API operations, least privilege, and Intune RBAC before authentication or policy writes are added.

No policy changes occur in this build. The UI does not provide a create/publish control. Never interpret local rule validation as proof that a rule is accepted by Intune.

See [Graph integration research](docs/GraphIntegration.md) for the endpoints supplied by the brief, research links, and unresolved verification items.

## Test status

Automated tests cover local SHA256 rule validation, supported application file extensions, and the policy rule count boundary. They do not cover a real Windows EPM Tools installation, Explorer registration, Microsoft Entra authentication, or Graph. Those integration checks require Windows and, for Graph, a separately authorized test tenant.
