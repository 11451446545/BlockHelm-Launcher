using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Launcher.Infrastructure.Updates;

namespace Launcher.UpdateBootstrap;

internal static class Program
{
    private static int Main(string[] args)
    {
        LauncherUpdateApplyOptions? options = null;
        try
        {
            options = LauncherUpdateApplyOptions.Parse(args);
            if (options is null || !Environment.Is64BitOperatingSystem
                || !string.Equals(options.SourcePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(options.TargetPath)
                || !string.Equals(Path.GetExtension(options.TargetPath), ".exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(options.TargetPath, options.SourcePath, StringComparison.OrdinalIgnoreCase))
                return 2;

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = UniversalUpdatePayload.SelectResource(Environment.OSVersion.Version);
            using var metadata = assembly.GetManifestResourceStream("BlockHelm.Update.payloads.json")
                ?? throw new InvalidDataException("Update metadata is missing.");
            var payloads = JsonSerializer.Deserialize<UniversalUpdatePayload[]>(metadata)
                ?? throw new InvalidDataException("Update metadata is invalid.");
            var payload = payloads.Single(item => item.ResourceName == resourceName);
            using var source = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException("Update payload is missing.");
            var extracted = payload.ExtractVerified(source, Path.GetDirectoryName(options.SourcePath)!);

            // The chosen launcher itself implements --recover-update. Keeping it
            // in the update cache also preserves the existing recovery contract.
            return new LauncherUpdateApplyRunner().Run(options with { SourcePath = extracted });
        }
        catch (Exception exception)
        {
            if (options is not null)
            {
                try
                {
                    Directory.CreateDirectory(options.LogDirectory);
                    File.AppendAllText(Path.Combine(options.LogDirectory, "update-bootstrap.log"),
                        $"{DateTimeOffset.Now:O} Update preparation failed: {exception}{Environment.NewLine}");
                    // Preparation never changes the target. Restore the user's
                    // original session if the old launcher already exited.
                    if (options.Restart && File.Exists(options.TargetPath))
                    {
                        if (options.ProcessId > 0)
                        {
                            try
                            {
                                using var oldProcess = Process.GetProcessById(options.ProcessId);
                                if (!oldProcess.WaitForExit(30_000)) return 1;
                            }
                            catch (ArgumentException) { }
                        }
                        using var restarted = Process.Start(new ProcessStartInfo(options.TargetPath)
                        {
                            UseShellExecute = false,
                            WorkingDirectory = Path.GetDirectoryName(options.TargetPath)!
                        });
                    }
                }
                catch { /* Preserve the original failure and leave the old EXE intact. */ }
            }
            return 1;
        }
    }
}
