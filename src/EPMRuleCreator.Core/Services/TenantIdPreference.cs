namespace EPMRuleCreator.Core.Services;

public static class TenantIdPreference
{
    public static string ForDisplay(string? tenantId) => string.IsNullOrWhiteSpace(tenantId)
        || string.Equals(tenantId.Trim(), "organizations", StringComparison.OrdinalIgnoreCase)
        ? string.Empty : tenantId.Trim();
}
