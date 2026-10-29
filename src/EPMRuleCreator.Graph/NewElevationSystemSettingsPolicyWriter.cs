using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace EPMRuleCreator.Graph;

internal sealed class NewElevationSystemSettingsPolicyWriter(HttpClient httpClient)
{
    internal async Task<string> CreateAsync(string accessToken, string name, string? description,
        bool networkSettingsEnabled,
        bool automatic, bool requireBusinessJustification, bool requireWindowsAuthentication,
        CancellationToken cancellationToken = default)
    {
        var body = NewElevationSystemSettingsPolicyPayload.Build(name, description, networkSettingsEnabled, automatic,
            requireBusinessJustification, requireWindowsAuthentication);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://graph.microsoft.com/beta/deviceManagement/configurationPolicies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try { response = await httpClient.SendAsync(request, cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw new InvalidOperationException("The policy creation result could not be received. Check Intune before retrying to avoid creating a duplicate policy.", exception);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                try
                {
                    if (JsonNode.Parse(text) is JsonObject error && error["error"] is JsonObject details)
                        message = details["message"]?.GetValue<string>();
                }
                catch (System.Text.Json.JsonException) { }
                throw new HttpRequestException($"Graph rejected policy creation ({(int)response.StatusCode}): {message ?? "No error details were returned."}", null, response.StatusCode);
            }

            JsonObject? document = null;
            try { document = JsonNode.Parse(text) as JsonObject; }
            catch (System.Text.Json.JsonException) { }
            var id = document?["id"]?.GetValue<string>();
            if (!Guid.TryParse(id, out _))
                throw new InvalidOperationException("Graph accepted policy creation but returned no valid policy ID. Check Intune before retrying to avoid creating a duplicate policy.");
            return id!;
        }
    }
}
