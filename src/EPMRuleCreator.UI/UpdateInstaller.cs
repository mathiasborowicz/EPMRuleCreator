using System.Diagnostics;
using System.IO;
using System.Text;
using EPMRuleCreator.Core.Services;

namespace EPMRuleCreator.UI;

internal static class UpdateInstaller
{
    internal static async Task StartAsync(ReleaseUpdate update, CancellationToken cancellationToken)
    {
        var destination = Environment.ProcessPath;
        if (destination is null || !Path.GetFileName(destination).Equals("EPMRuleCreator.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Automatic updates require the standalone EPMRuleCreator.exe.");
        var stage = Directory.CreateTempSubdirectory("EPMRuleCreator-update-").FullName;
        var archive = Path.Combine(stage, "update.zip");
        var executable = Path.Combine(stage, "EPMRuleCreator.exe");
        var handedOff = false;
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EPMRuleCreator/update-download");
            await UpdatePackageService.DownloadAsync(client, update, archive, cancellationToken);
            UpdatePackageService.ExtractExecutable(archive, executable);
            var fileVersion = FileVersionInfo.GetVersionInfo(executable);
            if (new Version(fileVersion.FileMajorPart, fileVersion.FileMinorPart, fileVersion.FileBuildPart) != update.Version
                || fileVersion.ProductName != "EPMRuleCreator")
                throw new InvalidDataException("The downloaded application's identity or version does not match the release.");
            // Equivalent to Unblock-File for this one verified executable only.
            // HttpClient normally adds no MOTW, but handle a Zone.Identifier if present.
            File.Delete(executable + ":Zone.Identifier");
            // Probe write access before closing the app. The real replacement happens after exit.
            var probe = destination + ".update-" + Guid.NewGuid().ToString("N") + ".tmp";
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            cancellationToken.ThrowIfCancellationRequested();
            static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            using var scriptStream = typeof(UpdateInstaller).Assembly.GetManifestResourceStream("EPMRuleCreator.Install-AppUpdate.ps1")
                ?? throw new InvalidOperationException("The update helper is missing.");
            using var reader = new StreamReader(scriptStream);
            var script = $"$destination = {Quote(destination)}\n$stage = {Quote(stage)}\n$appProcessId = {Environment.ProcessId}\n" + await reader.ReadToEndAsync(cancellationToken);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = stage
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the update helper.");
            try
            {
                var timer = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(stage, "helper-ready")))
                {
                    if (process.HasExited || timer.Elapsed > TimeSpan.FromSeconds(10))
                        throw new InvalidOperationException("The update helper could not start. Your current app remains unchanged.");
                    await Task.Delay(100, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            handedOff = true;
        }
        finally
        {
            if (!handedOff)
            {
                File.Delete(executable);
                File.Delete(archive);
                File.Delete(Path.Combine(stage, "helper-ready"));
                Directory.Delete(stage);
            }
        }
    }

}
