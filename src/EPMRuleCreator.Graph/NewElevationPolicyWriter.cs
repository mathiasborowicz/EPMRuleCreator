using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EPMRuleCreator.Core.Models;

namespace EPMRuleCreator.Graph;

internal sealed class NewElevationPolicyWriter(HttpClient httpClient)
{
    internal async Task<ElevationRulePolicy> CreateAsync(string accessToken, string name, string? description,
        ElevationRule rule, CancellationToken cancellationToken = default)
    {
        var body = NewElevationPolicyPayload.Build(name, description, rule);
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
            string text;
            try { text = await response.Content.ReadAsStringAsync(cancellationToken); }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                throw new InvalidOperationException("The policy creation response could not be read. Check Intune before retrying to avoid creating a duplicate policy.", exception);
            }
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                try
                {
                    if (JsonNode.Parse(text) is JsonObject error && error["error"] is JsonObject details)
                        message = GetString(details, "message");
                }
                catch (System.Text.Json.JsonException) { }
                throw new HttpRequestException($"Graph rejected policy creation ({(int)response.StatusCode}): {message ?? "No error details were returned."}", null, response.StatusCode);
            }

            JsonObject? document = null;
            try { document = JsonNode.Parse(text) as JsonObject; }
            catch (System.Text.Json.JsonException) { }
            var id = GetString(document, "id");
            if (!Guid.TryParse(id, out _))
                throw new InvalidOperationException("Graph accepted policy creation but returned no valid policy ID. Check Intune before retrying to avoid creating a duplicate policy.");

            return new ElevationRulePolicy(id!, GetString(document, "name") ?? name.Trim(),
                description, "windows10", "endpointPrivilegeManagement",
                DateTimeOffset.TryParse(document?["lastModifiedDateTime"]?.ToString(), out var modified) ? modified : null,
                1, false);
        }
    }

    private static string? GetString(JsonObject? document, string key) =>
        document?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
