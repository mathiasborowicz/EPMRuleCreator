namespace EPMRuleCreator.Core.Models;

public sealed record LocalAppSettings(string? ClientId, string? TenantId, bool? IsDarkTheme = null, bool CheckForUpdatesAtStartup = true);
