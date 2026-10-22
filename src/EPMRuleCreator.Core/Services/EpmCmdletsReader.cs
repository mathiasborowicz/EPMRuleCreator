using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace EPMRuleCreator.Core.Services;

public interface IEpmCmdletsReader
{
    bool IsAvailable { get; }
    Task<IReadOnlyDictionary<string, string?>> ReadAsync(string path, CancellationToken cancellationToken);
}

public sealed class EpmCmdletsReader : IEpmCmdletsReader
{
    public const string ModulePath = @"C:\Program Files\Microsoft EPM Agent\EpmTools\EpmCmdlets.dll";
    public bool IsAvailable => OperatingSystem.IsWindows() && File.Exists(ModulePath);

    public async Task<IReadOnlyDictionary<string, string?>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("EpmCmdlets.dll was not found.");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new InvalidOperationException("Windows PowerShell 5.1 was not found.");

        const string script = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $p=ConvertFrom-Json -InputObject ([Console]::In.ReadToEnd()); Import-Module -Name $p.Module -ErrorAction Stop; $r=Get-FileAttributes -FilePath $p.Path -CertOutputPath $p.CertOutputPath -ErrorAction Stop; ConvertTo-Json -InputObject $r -Depth 10 -Compress";
        var certificateOutputPath = Path.Combine(Path.GetTempPath(), $"EPMRuleCreator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certificateOutputPath);
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script })
            start.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = start };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                var input = JsonSerializer.Serialize(new { Module = ModulePath, Path = path, CertOutputPath = certificateOutputPath });
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                // Observe redirected-stream tasks before disposing the process.
                try { await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                throw;
            }
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"EPM Tools could not analyze this file. {error.Trim()}");

            try
            {
                using var document = JsonDocument.Parse(output);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array) root = root.EnumerateArray().FirstOrDefault();
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a JSON object from Get-FileAttributes.");
                return root.EnumerateObject().ToDictionary(property => property.Name,
                    property => property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : property.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("EPM Tools returned an unrecognized response.", exception);
            }
        }
        finally
        {
            try { Directory.Delete(certificateOutputPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
