using System.IO;
using System.Security.Cryptography;

namespace Launcher.Infrastructure.Updates;

// The legacy manifest accepts exactly one EXE. That EXE carries both launchers
// and passes the selected, verified payload to the existing transactional updater.
public sealed record UniversalUpdatePayload(string ResourceName, long Size, string Sha256)
{
    public static string SelectResource(Version windowsVersion) => windowsVersion switch
    {
        { Major: 6, Minor: >= 1 and <= 3 } => "BlockHelm.Update.win7.exe",
        { Major: >= 10 } => "BlockHelm.Update.modern.exe",
        _ => throw new PlatformNotSupportedException("Unsupported Windows version for this update.")
    };

    public string ExtractVerified(Stream source, string updateDirectory)
    {
        if (ResourceName is not ("BlockHelm.Update.win7.exe" or "BlockHelm.Update.modern.exe")
            || Size is <= 0 or > 512L * 1024 * 1024
            || Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid embedded update metadata.");

        var directory = Path.Combine(Path.GetFullPath(updateDirectory), "payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "BlockHelm_Launcher_x64.exe");
        try
        {
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long length = 0;
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    length += count;
                    if (length > Size) throw new InvalidDataException("Embedded update exceeds declared size.");
                    hash.AppendData(buffer, 0, count);
                    output.Write(buffer, 0, count);
                }
                if (length != Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Embedded update checksum mismatch.");
                output.Flush(flushToDisk: true);
            }
            return destination;
        }
        catch
        {
            // Only our newly created file/directory can be removed on failure.
            File.Delete(destination);
            Directory.Delete(directory);
            throw;
        }
    }
}
