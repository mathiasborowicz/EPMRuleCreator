# Microsoft Graph and EPM rule model

## Current implementation

The optional Graph connection lists Endpoint Privilege Management Elevation Rules policies, reads rule counts and reusable certificate settings, and can append a rule to a selected policy after user confirmation.

The app lists policies with `GET /beta/deviceManagement/configurationPolicies`, filtered by `templateReference/templateFamily eq 'endpointSecurityEndpointPrivilegeManagement'`, selecting the metadata shown in the UI. It follows `@odata.nextLink`. For the selected policy it reads `GET /beta/deviceManagement/configurationPolicies('{id}')/settings?$expand=settingDefinitions&$top=1000`, also following `@odata.nextLink`.

The test-tenant response supplied for this implementation confirms the root setting instance type `deviceManagementConfigurationGroupSettingCollectionInstance` and definition ID `device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}`. Rules are counted from the root instance's `groupSettingCollectionValue`; the supplied response contained two entries. Its expanded definition reported `maximumCount: 100` and `minimumCount: 1`. Policy-level `settingCount` is not the number of elevation rules.

The app requests the setting definition's `maximumCount` when available and otherwise uses the observed EPM limit of 100. A policy with existing settings but no identifiable EPM root collection is treated as an error rather than guessed to be empty.

Reusable settings are listed through `GET /beta/deviceManagement/reusablePolicySettings`. This collection also contains unrelated Intune settings, so the app filters client-side by the exact EPM certificate definition ID `device_vendor_msft_policy_privilegemanagement_reusablesettings_certificatefile`. The test tenant returned the EPM setting "Netextender" and an unrelated Defender setting; only the former should appear in the EPM certificate selector. Results are paged using `@odata.nextLink`.

For certificate rules, the app can use the extracted public X.509 signer certificate or reference a loaded EPM reusable certificate setting. Extraction does not validate Authenticode trust. For writes, the app refreshes the entire policy settings collection, clones an existing rule with the matching hash/publisher/reusable-certificate shape, changes the selected values, and PATCHes the existing root setting. Existing rules are preserved. After confirmation, it rereads the policy and checks the count. If Graph rejects the write or verification fails, the UI reports the result; after a verification failure users should check Intune before retrying.

Current safeguards/constraints: writes require an existing EPM root setting and an existing rule of the same identification/certificate-source shape to use as a payload template. The app does not create the initial root setting or create a policy. The 100-rule limit is reread immediately before PATCH. If Graph includes `@odata.etag` in the setting representation, the app sends `If-Match`; the provided tenant response did not include it, so concurrent edits by another administrator cannot be fully excluded. `DenyAll` child-process behavior is intentionally rejected because its choice value was not present in the supplied tenant examples. The write path still needs a live end-to-end test against the user's test tenant before community release.

## Permissions

The app requests delegated `User.Read` (signed-in identity) and `DeviceManagementConfiguration.ReadWrite.All` (policy/settings reads and planned writes). Admin consent is required. The setting create/update beta API documentation identifies this permission; `DeviceManagementEndpointSecurity.ReadWrite.All` is not needed for these configuration-policy settings endpoints.

- [Create a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-create?view=graph-rest-beta)
- [Update a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-update?view=graph-rest-beta)
- [Permission reference](https://learn.microsoft.com/en-us/graph/permissions-reference#devicemanagementconfigurationreadwriteall)
- [List reusable policy settings](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementreusablepolicysetting-list?view=graph-rest-beta)
- [Reusable policy setting resource](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfigv2-devicemanagementreusablepolicysetting?view=graph-rest-beta)

Graph consent does not replace Intune licensing or the signed-in user's Intune RBAC role and scope-tag access.

## Remaining before release

Validate actual PATCH acceptance and readback in the test tenant for hash, uploaded publisher certificate, reusable certificate reference, each supported elevation type, and both observed child-process choices. Confirm the server-side behavior for simultaneous admin edits; the example response had no ETag. Do not distribute as production-ready until these beta API integration cases pass.
