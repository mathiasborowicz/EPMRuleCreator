# From a file to an elevation rule

EPMRuleCreator helps you answer two questions: **What is this file?** and **How should Intune EPM identify and elevate it?** You can use the first part entirely on its own.

## 1. Inspect a file locally

Open the app, click **Choose file**, then **Analyze file**. Supported selections are `.exe`, `.msi`, and `.ps1`. The app reads the file; it never runs it.

The left side shows **File information**, including SHA256 and available Windows metadata. Not every file has a product name, version, or signature. Empty metadata does not mean the analysis failed.

The right side is your editable **Elevation rule**. Available values are prefilled to save typing. Changing files clears the old file details and rule fields, so you do not accidentally mix two applications. Your connected account and selected policy are retained.

You do not need to sign in, install the Intune agent, or run the app as administrator for basic analysis.

> 📸 **Screenshot placeholder: local analysis.** Show a signed sample application, File information, and the prefilled rule fields.
<!-- SCREENSHOT: overview. See Screenshots.md for filenames and redaction guidance. -->

### Do I need Use EpmCmdlets?

Usually not for getting started. The app independently reads SHA256, available metadata, and signing certificates. **Use EpmCmdlets** adds Microsoft's PowerShell `Get-FileAttributes` output when available; it is off by default.

The checkbox is disabled if `C:\Program Files\Microsoft EPM Agent\EpmTools\EpmCmdlets.dll` is missing. Importing that module can require an administrative PowerShell. If it fails, the tool records a warning and still completes its independent local analysis. It never elevates itself or installs the agent automatically. Do not assume every optional EpmCmdlets attribute is identical to a normal Windows metadata field.

## 2. Review or export the certificate chain

Under **Signing certificate**, review the available signer, intermediate issuers, and root. Hover for issuer, thumbprint, and validity details.

- **Export certificates…** creates a new subfolder and saves each public certificate separately as `.cer`, in chain order.
- **Export chain bundle…** saves the available certificates together as P7B or PEM.

The app uses certificates embedded in the file and local Windows stores. It does not download missing issuers or check revocation. If certificates are unavailable, the chain is marked **Incomplete** and only the available subset can be exported. No private keys are exported.

**A certificate being present, or its chain being complete, does not prove that the file's signature is trusted.** The card and exports describe the analyzed file, even if you choose a different certificate for the rule.

> 📸 **Screenshot placeholder: certificate chain.** Show certificate roles, completeness status, and both export buttons.
<!-- SCREENSHOT: certificate-chain. -->

## 3. Prepare the rule

Review the **Rule name**, optional **Rule description**, and file attributes. Rule description is administrative context; **File description** is a separate matching attribute from the file's metadata.

### Choose how EPM identifies the file

Use **Signature source**:

- **Not configured:** no certificate is used. **File name** and **File hash** become required. The hash is SHA256.
- **Upload a certificate file:** use **Use analyzed file certificate** if available, or **Choose certificate file…** to provide your own. The radio options show which one is selected.
- **Use a certificate file in reusable settings:** sign in and choose an EPM certificate from the dropdown. Unrelated Intune reusable settings are excluded.

With a certificate selected, **Certificate type** offers **Publisher** or **Certificate authority**. File name and hash are then optional, but think carefully about how broadly a certificate-only rule can match. A restricted file path that standard users cannot modify can reduce risk.

**Minimum version** is optional. If supplied, it must have exactly four numeric parts, such as `1.1.0.0`. Review the other optional matching attributes and leave them empty if they should not constrain the rule. Do not fill every field just to pass validation.

> 📸 **Screenshot placeholder: certificate choices.** Show Signature source, the analyzed/uploaded certificate radio options, and Certificate type.
<!-- SCREENSHOT: certificate-selection. -->

### Choose elevation behavior

**Elevation type** offers **User confirmed**, **Automatic**, **Support approved**, **Deny**, and **Elevate as current user**.

For **User confirmed**, choose **Business justification** and/or **Windows authentication** as needed. For **Elevate as current user**, Windows authentication is mandatory and business justification is the optional additional requirement. Use that mode only when the application's need for the current user's profile justifies it.

Also review **Child process behavior** and **Restrict Arguments**. Enabling **Allow** under argument restrictions exposes **Argument List**. These choices affect how broad the resulting elevation rule is; do not treat automatic elevation as a harmless default.

Click **Validate rule** to check the local inputs. Warnings and blocking errors appear in **Logs**. Passing local validation does not prove that Intune will accept or enforce the policy, and does not verify the trustworthiness of the analyzed file.

## 4. Connect to Intune, only when needed

Open the account icon at the top right. Enter your own **Application (client) ID** and **Tenant ID**, then choose **Sign in**. Credentials are entered in the system browser, not the app.

Configure your registration and consent first using the [Entra setup guide](GraphAuthentication.md). The app needs delegated `User.Read` and `DeviceManagementConfiguration.ReadWrite.All`; the latter requires admin consent. Intune licensing, user RBAC, and scope-tag access still apply.

Policies load after sign-in. Choose an existing **Elevation Rules policy**, or **+ Add new** to enter a new policy's name and optional description. Reusable EPM certificates load when that signature source is selected. You do not need matching example rules already in the tenant.

> 📸 **Screenshot placeholder: optional sign-in and policy selection.** Show a sanitized account panel and the policy dropdown with **+ Add new**.
<!-- SCREENSHOT: intune-policy. -->

### Upload deliberately

Review the selected tenant, policy, certificate source, and elevation behavior before clicking the upload button. **Upload writes immediately**, without another confirmation dialog.

- For an existing policy, the app reads its current settings, checks the 100-rule limit, and submits the collection with the new rule while preserving existing rules.
- For a new policy, **Create policy and upload rule** creates the policy with its first rule. It uses the default scope tag `0` and has no assignments. Configure assignments in Intune afterwards.

Progress, warnings, and success/errors appear in **Logs** below the certificate card. Test in a test tenant first. Graph beta behavior can change; simultaneous edits by another administrator are a known concern. [Technical behavior and limits](GraphIntegration.md).

## 5. Use it again

In installed mode, optionally check **Add to Explorer context menu** in setup, or use **Add menu** later for a right-click entry under Windows 11 **Show more options > Create EPM Rule**. It registers the installed EXE, not a second copy. **↻** repairs the installed registration, and **Remove menu** removes it. Portable mode has no context-menu integration.

Use the **Dark** switch for appearance and **Updates** for version checks or updating in place. The startup check can be disabled. [Modes, updates, and local preferences](Installation.md).

Logs stay in memory for the session, retain the latest 200 entries, and can be selected/copied or cleared. They are not automatically saved. Include relevant errors when reporting a problem, but redact personal paths, tenant/account details, and sensitive data first.
