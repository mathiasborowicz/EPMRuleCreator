# Optional Microsoft Graph authentication

The app uses MSAL as a native public client. Microsoft Graph PowerShell and the Graph CLI are not runtime dependencies. Sign-in is optional: file inspection and local rule preparation work without a client ID, tenant, network connection, or account.

## One-time Entra app registration

For optional Intune access, use an Entra app registration managed by you or your organization. The release contains no preconfigured publisher client ID or tenant ID. Enter the identifiers locally in the account panel; no registration is needed for offline file analysis.

1. Open the [Entra admin center](https://entra.microsoft.com/) and go to **App registrations > New registration**. Name it **EPMRuleCreator**, select **Accounts in this organizational directory only** for your own tenant, and register it.
2. Under **Authentication > Add a platform > Mobile and desktop applications**, add `http://localhost` and save.
3. Under **API permissions > Add a permission > Microsoft Graph > Delegated permissions**, add `User.Read` and `DeviceManagementConfiguration.ReadWrite.All`.
4. Select **Grant admin consent** for your organization using an account authorized to grant it.
5. From **Overview**, copy **Application (client) ID** and **Directory (tenant) ID** into the tool's account panel, then click **Sign in**.

For use across multiple organizations, choose **Accounts in any organizational directory** instead; each organization needs consent. `User.Read` displays your signed-in identity. `DeviceManagementConfiguration.ReadWrite.All` lets the app read and write the EPM policies. Your user still needs the appropriate Intune licensing, RBAC permissions, and scope-tag access. This is a public desktop client; no client secret is needed.

The tenant field starts empty. Leave it blank for organizational accounts across tenants, or enter a tenant GUID to restrict sign-in to one tenant. The app uses `organizations` internally for a blank value, but does not prefill that text, including when older settings saved it. Saved concrete tenant IDs are retained. After signing in, the account button shows your initials with a green connection indicator. Click it again to view the connected account or choose **Sign out**. To switch accounts or tenants, sign out, adjust the values if needed, and sign in again; MSAL shows account selection. Policies load automatically after sign-in. Click outside the panel or press Escape to close it.

## Runtime and token handling

- MSAL opens the system browser and uses the authorization-code flow with PKCE / loopback redirect; credentials are not collected by this app.
- MSAL uses its default client-instance cache in memory only. The app does not register file-persistence callbacks and no longer includes the MSAL persistence-extension package. It does not create, read, migrate, or write `msal.cache`. Tokens are never saved in `settings.json` or logs. Client/tenant identifiers and the theme preference are still saved locally.
- Closing the process ends this in-memory app session. Each new app run requires sign-in. **Sign out**, switching accounts, and failed sign-in release the current client cache; sign-out does not revoke Entra sessions, delete browser cookies, or revoke already-issued tokens. The system browser can still offer SSO without asking for a password. In-memory caching does not protect against malicious processes already running as the same user or make guarantees about Windows paging, hibernation, or process dumps.
- **Files left by earlier versions:** those builds used an on-disk cache encrypted with Windows DPAPI for the current user. DPAPI encrypts a file's contents; it does not remove the file when the app closes. The new version ignores those files and does not automatically delete them. After closing all app instances, you may remove only `msal.cache` from `%LOCALAPPDATA%\EPMRuleCreator` and/or `%LOCALAPPDATA%\IntuneEpmRuleCreator` in Explorer if present. Keep `settings.json` if you want to retain your identifiers and theme. Deleting an old cache requires a new sign-in for a build that used it; it does not revoke server-side tokens or browser sessions. Do not share cache files.
- The connection calls `GET https://graph.microsoft.com/v1.0/me` with `User.Read` and lists policies and settings through Graph beta with `DeviceManagementConfiguration.ReadWrite.All`.
- Intune policy writes occur only when the user explicitly clicks the upload button, without an additional confirmation dialog. Progress, warnings, and results appear in the session log below the signing certificate chain. In the policy dropdown, **+ Add new** opens a name and optional description; **Create policy and upload rule** creates the policy with its first rule. This uses the same delegated `DeviceManagementConfiguration.ReadWrite.All` permission. New policies use the default scope tag (`0`) and are created without assignments. Intune RBAC and scope-tag access remain separate authorization requirements; see [Graph integration research](GraphIntegration.md).
- Only the Microsoft Graph global cloud is implemented. Sovereign cloud authorities and Graph endpoints are not supported yet.

If Graph sign-in is skipped, fails, or has no network access, all local analysis and validation features remain usable.
