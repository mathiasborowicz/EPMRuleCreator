# Optional Microsoft Graph authentication

The app uses MSAL as a native public client. Microsoft Graph PowerShell and the Graph CLI are not runtime dependencies. Sign-in is optional: file inspection and local rule preparation work without a client ID, tenant, network connection, or account.

## One-time Entra app registration

The project publisher must create an app registration before distributing a community release. End users do not need to create their own registration once the publisher's application ID is configured in the release.

1. In the Microsoft Entra admin center, create an app registration for the application.
2. Choose **Accounts in any organizational directory** if the community release should support multiple customer tenants. Personal Microsoft accounts are not needed for Intune.
3. Under **Authentication**, add the **Mobile and desktop applications** platform and the redirect URI `http://localhost`. This is a public native client; do not create or ship a client secret.
4. Add the delegated Microsoft Graph permissions `User.Read` and `DeviceManagementConfiguration.ReadWrite.All`. `User.Read` displays the signed-in user's name and UPN. `DeviceManagementConfiguration.ReadWrite.All` is used to list policies/read settings and append a rule to a selected policy. Admin consent is required.
5. Keep the Application (client) ID out of the executable and source repository. Click the circular **EN** account button at the top right to open the optional sign-in panel and enter the client ID and tenant. Choose **Sign in** to connect. The app stores these values in the current user's local settings file; neither value is a credential, and no secret is stored there.

The tenant field accepts `organizations` for organizational accounts across tenants, or a tenant GUID to restrict sign-in to one tenant. After signing in, the account button shows your initials with a green connection indicator. Click it again to view the connected account or choose **Sign out**. To switch accounts or tenants, sign out, adjust the values if needed, and sign in again; MSAL shows account selection. Policies load automatically after sign-in. Click outside the panel or press Escape to close it.

## Runtime and token handling

- MSAL opens the system browser and uses the authorization-code flow with PKCE / loopback redirect; credentials are not collected by this app.
- The token cache is persisted under the current user's Local AppData using the operating system's protected persistence (DPAPI on Windows). The app never writes access tokens to its settings file or logs.
- **Sign out** removes the current account from the app's MSAL cache. It does not revoke the user's Entra sessions in the browser or other applications.
- The connection calls `GET https://graph.microsoft.com/v1.0/me` with `User.Read` and lists policies and settings through Graph beta with `DeviceManagementConfiguration.ReadWrite.All`.
- Intune policy writes occur only after an explicit upload action and confirmation. Intune RBAC and scope-tag access remain separate authorization requirements; see [Graph integration research](GraphIntegration.md).
- Only the Microsoft Graph global cloud is implemented. Sovereign cloud authorities and Graph endpoints are not supported yet.

If Graph sign-in is skipped, fails, or has no network access, all local analysis and validation features remain usable.
