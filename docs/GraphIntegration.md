# Microsoft Graph and EPM rule model

## Verification status

Graph policy creation and rule writes are intentionally not implemented in this build. The project request requires verifying the EPM setting definitions, nested setting instance types, collection update semantics, and permission requirements from current Microsoft documentation before sending writes. The Microsoft Learn pages supplied with the request returned HTTP 403 from the onboarding runner on 2026-10-21, including:

- [Create a device management configuration policy](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationpolicy-create?view=graph-rest-beta)
- [Create a device management configuration setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-create?view=graph-rest-beta)
- [Create elevation rules for Endpoint Privilege Management](https://learn.microsoft.com/en-us/intune/epm/create-elevation-rules)

The resulting schema details could not be verified. No setting definition ID, template ID, payload shape, or write permission has been guessed. This is a release blocker for the Intune publishing workflow, not a claim that the feature is unsupported by Microsoft Graph.

## Known API surface from the supplied references

The requested policy resource is `deviceManagement/configurationPolicies`, with settings beneath a policy. The links supplied point to Graph beta references. This establishes the resources to investigate, but does not by itself establish the EPM rule collection representation or a safe update strategy.

Before implementing writes, verify from current first-party documentation and a supported schema source:

1. How an EPM Elevation Rules policy is identified and filtered from other configuration policies.
2. The exact template, platform, technology, setting definition, and setting instance identifiers and types.
3. How individual rules are represented, including certificate data and child-process behavior.
4. Whether rules are one collection setting and how the complete collection must be read and updated.
5. ETag or other concurrency behavior, the rule limit, and post-write readback.
6. Delegated Graph scopes and Intune RBAC roles for read and write operations.

Until those points are verified, this application performs no Graph authentication, policy listing, policy creation, or rule writes. It does not request Graph permissions.

## Permission candidates to verify

The project brief names `DeviceManagementConfiguration.Read.All`, `DeviceManagementConfiguration.ReadWrite.All`, and `DeviceManagementEndpointSecurity.ReadWrite.All` as candidates. None is declared by this build: the exact least-privilege permission must be established from the documentation for the specific configuration policy operations. Intune RBAC authorization is also separate from delegated Graph scopes and must be documented and tested independently.
