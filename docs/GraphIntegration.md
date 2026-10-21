# Microsoft Graph and EPM rule model

## Current implementation

The optional Graph connection lists Endpoint Privilege Management Elevation Rules policies and can read the number of rules in the selected policy. It does not create or modify policy settings yet.

The app lists policies with `GET /beta/deviceManagement/configurationPolicies`, filtered by `templateReference/templateFamily eq 'endpointSecurityEndpointPrivilegeManagement'`, selecting the metadata shown in the UI. It follows `@odata.nextLink`. For the selected policy it reads `GET /beta/deviceManagement/configurationPolicies('{id}')/settings?$expand=settingDefinitions&$top=1000`, also following `@odata.nextLink`.

The test-tenant response supplied for this implementation confirms the root setting instance type `deviceManagementConfigurationGroupSettingCollectionInstance` and definition ID `device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}`. Rules are counted from the root instance's `groupSettingCollectionValue`; the supplied response contained two entries. Its expanded definition reported `maximumCount: 100` and `minimumCount: 1`. Policy-level `settingCount` is not the number of elevation rules.

The app requests the setting definition's `maximumCount` when available and otherwise uses the observed EPM limit of 100. A policy with existing settings but no identifiable EPM root collection is treated as an error rather than guessed to be empty.

## Permissions

The app requests delegated `User.Read` (signed-in identity) and `DeviceManagementConfiguration.ReadWrite.All` (policy/settings reads and planned writes). Admin consent is required. The setting create/update beta API documentation identifies this permission; `DeviceManagementEndpointSecurity.ReadWrite.All` is not needed for these configuration-policy settings endpoints.

- [Create a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-create?view=graph-rest-beta)
- [Update a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-update?view=graph-rest-beta)
- [Permission reference](https://learn.microsoft.com/en-us/graph/permissions-reference#devicemanagementconfigurationreadwriteall)

Graph consent does not replace Intune licensing or the signed-in user's Intune RBAC role and scope-tag access.

## Remaining before writes

Before sending writes, verify the complete nested payload and whether the API safely supports appending a rule or requires replacing the whole collection. Concurrency/ETag handling and post-write readback also need validation in the test tenant. The current build sends no policy-write request.
