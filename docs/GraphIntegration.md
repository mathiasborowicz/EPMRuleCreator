# Microsoft Graph and EPM rule model

## Current implementation

The optional Graph connection lists Endpoint Privilege Management Elevation Rules policies, reads rule counts and reusable certificate settings, and can append a rule to a selected policy after user confirmation.

The app lists policies with `GET /beta/deviceManagement/configurationPolicies`, filtered by `templateReference/templateFamily eq 'endpointSecurityEndpointPrivilegeManagement'`, selecting the metadata shown in the UI. It follows `@odata.nextLink`. For the selected policy it reads `GET /beta/deviceManagement/configurationPolicies('{id}')/settings?$expand=settingDefinitions&$top=1000`, also following `@odata.nextLink`.

The test-tenant response supplied for this implementation confirms the root setting instance type `deviceManagementConfigurationGroupSettingCollectionInstance` and definition ID `device_vendor_msft_policy_privilegemanagement_elevationrules_{elevationrulename}`. Rules are counted from the root instance's `groupSettingCollectionValue`; the supplied response contained two entries. Its expanded definition reported `maximumCount: 100` and `minimumCount: 1`. Policy-level `settingCount` is not the number of elevation rules.

The app requests the setting definition's `maximumCount` when available and otherwise uses the observed EPM limit of 100. A policy with existing settings but no identifiable EPM root collection is treated as an error rather than guessed to be empty.

Reusable settings are listed through `GET /beta/deviceManagement/reusablePolicySettings`. This collection also contains unrelated Intune settings, so the app filters client-side by the exact EPM certificate definition ID `device_vendor_msft_policy_privilegemanagement_reusablesettings_certificatefile`. The test tenant returned the EPM setting "Netextender" and an unrelated Defender setting; only the former should appear in the EPM certificate selector. Results are paged using `@odata.nextLink`.

For certificate rules, the app can use the extracted public X.509 signer certificate or reference a loaded EPM reusable certificate setting. Extraction does not validate Authenticode trust. For writes, the app refreshes the policy and all settings, clones an existing rule with the matching hash/publisher/reusable-certificate shape, changes the selected values, and sends a `PUT` to the policy resource with the policy fields and complete settings collection. The setting objects are serialized as `@odata.type` plus `settingInstance`, matching the request observed in the Intune admin center browser. This preserves the other settings and existing rules. After confirmation, it rereads the policy and checks the count. If Graph rejects the write or verification fails, the UI reports the result; after a verification failure users should check Intune before retrying.

Important API discrepancy: Microsoft Graph's published beta documentation currently specifies `PATCH /deviceManagement/configurationPolicies/{policyId}/settings/{settingId}` for an individual setting and `PATCH /deviceManagement/configurationPolicies/{policyId}` for a policy. In the user's test tenant, the documented setting PATCH returns a routing error; the request captured from the Intune admin center instead uses `PUT /deviceManagement/configurationPolicies('{policyId}')` with a full policy body. The app follows that observed request, but the PUT behavior is not listed in the public update documentation and remains subject to live validation in the user's tenant.

Current safeguards/constraints: writes require an existing EPM root setting and a rule with the requested elevation type as a payload template. This preserves choice-specific child settings: user-confirmed rules use separate optional collections for Windows authentication and business justification; current-user rules use the observed validation collection, always selecting Windows authentication and optionally business justification. Certificate rules expose the two observed certificate types (`publisher` and `issuingauthority`) and apply the selected type in the payload. Certificate source settings are composed from an existing rule in the same policy with a matching publisher or reusable-certificate source; the matching certificate source need not already use the requested elevation type. Child-process behavior supports the observed allow, require-rule, and deny choices; `NotConfigured` is represented by omitting that optional setting instance. Empty optional text settings are omitted instead of being sent as empty strings. The supplied Graph file-version definition accepts an empty string or exactly four numeric components; when the UI contains three components, the upload appends `.0` (for example, `1.1.0` becomes `1.1.0.0`) to satisfy that pattern. The app does not create the initial root setting or create a policy. The 100-rule limit is reread immediately before PUT. The policy is read again immediately before constructing the replacement body, but concurrent edits after that read cannot be excluded. The write path still needs a live end-to-end test against the user's test tenant before community release.

When hash identification is selected (`signaturesource` not configured), file name and SHA256 hash are required. When a publisher or reusable certificate is selected, those two fields are optional; if left blank, their setting instances are removed from the rule payload rather than serialized with empty values. The optional minimum file version is also omitted when blank.

## Permissions

The app requests delegated `User.Read` (signed-in identity) and `DeviceManagementConfiguration.ReadWrite.All` (policy/settings reads and planned writes). Admin consent is required. The setting create/update beta API documentation identifies this permission; `DeviceManagementEndpointSecurity.ReadWrite.All` is not needed for these configuration-policy settings endpoints.

- [Create a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-create?view=graph-rest-beta)
- [Update a configuration-policy setting](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsetting-update?view=graph-rest-beta)
- [Permission reference](https://learn.microsoft.com/en-us/graph/permissions-reference#devicemanagementconfigurationreadwriteall)
- [List reusable policy settings](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementreusablepolicysetting-list?view=graph-rest-beta)
- [Reusable policy setting resource](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfigv2-devicemanagementreusablepolicysetting?view=graph-rest-beta)

Graph consent does not replace Intune licensing or the signed-in user's Intune RBAC role and scope-tag access.

## Remaining before release

Validate actual PUT acceptance and readback in the test tenant for hash, uploaded publisher certificate, reusable certificate reference, each supported elevation type, and both observed child-process choices. Confirm the server-side behavior for simultaneous admin edits; no concurrency token was included in the supplied payload. Do not distribute as production-ready until these beta API integration cases pass.
