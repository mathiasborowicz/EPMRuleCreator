# Optional Microsoft Graph authentication

The app uses MSAL as a native public client. Microsoft Graph PowerShell and the Graph CLI are not runtime dependencies. Sign-in is optional: file inspection and local rule preparation work without a client ID, tenant, network connection, or account.

## One-time Entra app registration

The project publisher must create an app registration before distributing a community release. End users do not need to create their own registration once the publisher's application ID is configured in the release.

1. In the Microsoft Entra admin center, create an app registration for the application.
2. Choose **Accounts in any organizational directory** if the community release should support multiple customer tenants. Personal Microsoft accounts are not needed for Intune.
3. Under **Authentication**, add the **Mobile and desktop applications** platform and the redirect URI `http://localhost`. This is a public native client; do not create or ship a client secret.
4. Add the delegated Microsoft Graph permission `User.Read`. It is used only to display the signed-in user's name and UPN after authentication. Admin consent may be required by the tenant's consent policy.
5. Copy the Application (client) ID. For development builds, enter it in the app's optional Graph connection panel. For a community release, the publisher can bake the public identifier into the single-file build so users do not have to supply it themselves. Build with `-p:GraphClientId=<application-client-id>`; a client ID is not a secret. For example:

   ```powershell
   $clientId = "00000000-0000-0000-0000-000000000000" # replace with the registration's client ID
   dotnet publish .\src\IntuneEpmRuleCreator.UI\IntuneEpmRuleCreator.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true "-p:GraphClientId=$clientId" -o .\publish\win-x64
   ```

   Leave `GraphClientId` unset for a generic development build; users can enter their own test registration ID.

The tenant field accepts `organizations` for organizational accounts across tenants, or a tenant GUID to restrict sign-in to one tenant. Entering a different tenant and signing in again switches the authority; **Sign in / switch account** asks MSAL to show account selection.

## Runtime and token handling

- MSAL opens the system browser and uses the authorization-code flow with PKCE / loopback redirect; credentials are not collected by this app.
- The token cache is persisted under the current user's Local AppData using the operating system's protected persistence (DPAPI on Windows). The app never writes access tokens to its settings file or logs.
- **Sign out** removes the current account from the app's MSAL cache. It does not revoke the user's Entra sessions in the browser or other applications.
- The connection currently calls `GET https://graph.microsoft.com/v1.0/me` with delegated `User.Read` to confirm identity and display the account and tenant ID.
- No Intune policy is listed, created, or modified by this authentication milestone. `User.Read` does not authorize Intune operations. Least-privilege Intune delegated permissions and Intune RBAC must be established separately after verifying the EPM API schema; see [Graph integration research](GraphIntegration.md).
- Only the Microsoft Graph global cloud is implemented. Sovereign cloud authorities and Graph endpoints are not supported yet.

If Graph sign-in is skipped, fails, or has no network access, all local analysis and validation features remain usable.
