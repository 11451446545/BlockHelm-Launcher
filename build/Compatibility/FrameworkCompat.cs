// Framework API adapters. net8 keeps the original implementations; net6 uses equivalent APIs.
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

#if BHL_COMPAT_Launcher_App
namespace Launcher.App.Compatibility;
#elif BHL_COMPAT_Launcher_Application
namespace Launcher.Application.Compatibility;
#else
namespace Launcher.Infrastructure.Compatibility;
#endif

internal static class FrameworkCompat
{
    public static void NotNullOrWhiteSpace(string? value, [CallerArgumentExpression("value")] string? name = null)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
#else
        if (value is null) throw new ArgumentNullException(name);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("The value cannot be empty or whitespace.", name);
#endif
    }

    public static void Positive(long value, [CallerArgumentExpression("value")] string? name = null)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
#else
        if (value <= 0) throw new ArgumentOutOfRangeException(name, value, "The value must be positive.");
#endif
    }

    public static void ThrowIfDisposed(bool condition, object instance)
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(condition, instance);
#else
        if (condition) throw new ObjectDisposedException(instance.GetType().FullName);
#endif
    }

    public static TimeSpan GetElapsedTime(long start)
    {
#if NET7_0_OR_GREATER
        return Stopwatch.GetElapsedTime(start);
#else
        return TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency);
#endif
    }

    public static Task CancelAsync(CancellationTokenSource source)
    {
#if NET8_0_OR_GREATER
        return source.CancelAsync();
#else
        return Task.Run(() => source.Cancel());
#endif
    }

    public static byte[] ComputeHash(HashAlgorithmName algorithm, Stream stream)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[81920];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
            hash.AppendData(buffer, 0, count);
        return hash.GetHashAndReset();
    }

    public static async ValueTask<byte[]> ComputeHashAsync(HashAlgorithmName algorithm, Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            hash.AppendData(buffer, 0, count);
        return hash.GetHashAndReset();
    }

    public static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
#if NET7_0_OR_GREATER
        await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
#else
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[total..], token).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException();
            total += count;
        }
#endif
    }

    public static async Task<string> ReadToEndAsync(TextReader reader, CancellationToken token)
    {
#if NET8_0_OR_GREATER
        return await reader.ReadToEndAsync(token).ConfigureAwait(false);
#else
        token.ThrowIfCancellationRequested();
        return await reader.ReadToEndAsync().WaitAsync(token).ConfigureAwait(false);
#endif
    }

    public static string ExportSubjectPublicKeyInfoPem(RSA key)
    {
#if NET7_0_OR_GREATER
        return key.ExportSubjectPublicKeyInfoPem();
#else
        return new string(PemEncoding.Write("PUBLIC KEY", key.ExportSubjectPublicKeyInfo()));
#endif
    }
}
