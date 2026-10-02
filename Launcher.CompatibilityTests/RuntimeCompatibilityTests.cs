using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Xunit;
using Compat = Launcher.Infrastructure.Compatibility.FrameworkCompat;

namespace Launcher.CompatibilityTests;

public sealed class RuntimeCompatibilityTests
{
    [Theory]
    [InlineData("MD5")]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("SHA512")]
    public async Task StreamingChecksumsMatchOriginalPayload(string algorithm)
    {
        var payload = Enumerable.Range(0, 250003).Select(index => (byte)(index % 251)).ToArray();
        using var hasher = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm));
        hasher.AppendData(payload);
        var expected = hasher.GetHashAndReset();
        using var first = new MemoryStream(payload);
        Assert.Equal(expected, Compat.ComputeHash(new HashAlgorithmName(algorithm), first));
        using var second = new FragmentedStream(payload);
        Assert.Equal(expected, await Compat.ComputeHashAsync(new HashAlgorithmName(algorithm), second, default));
    }

    [Fact]
    public async Task ExactReadsHandleFragmentedNetworkPacketsAndRejectTruncation()
    {
        var payload = Encoding.UTF8.GetBytes("Minecraft skin payload");
        using var source = new FragmentedStream(payload);
        var actual = new byte[payload.Length];
        await Compat.ReadExactlyAsync(source, actual, default);
        Assert.Equal(payload, actual);
        await Assert.ThrowsAsync<EndOfStreamException>(() => Compat.ReadExactlyAsync(source, new byte[1], default).AsTask());
    }

    [Fact]
    public async Task CancelledIoDoesNotContinueDownloading()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var source = new FragmentedStream(new byte[1024]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Compat.ComputeHashAsync(HashAlgorithmName.SHA256, source, cancellation.Token).AsTask());
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public async Task AsyncCancellationCompletesRegisteredCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var cleaned = false;
        using var registration = cancellation.Token.Register(() => cleaned = true);
        await Compat.CancelAsync(cancellation);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(cleaned);
    }

    [Fact]
    public void ExportedOfflineSkinPublicKeyRoundTrips()
    {
        using var key = RSA.Create(2048);
        using var imported = RSA.Create();
        imported.ImportFromPem(Compat.ExportSubjectPublicKeyInfoPem(key));
        var payload = Encoding.UTF8.GetBytes("signed texture");
        var signature = key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(imported.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void OriginalUiFontsAreAvailableFromEmbeddedResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Registers WPF's pack URI handler without opening a window.
                _ = new System.Windows.Application();
                foreach (var name in new[] { "Microsoft YaHei UI", "Segoe MDL2 Assets" })
                {
                    var family = Launcher.App.Compatibility.FontCompatibility.GetEmbeddedFamily(name);
                    var face = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                    Assert.True(face.TryGetGlyphTypeface(out var glyph), "Embedded family failed: " + name + "; found: " +
                        string.Join(" | ", Fonts.GetFontFamilies(new Uri("pack://application:,,,/BlockHelm_Launcher_x64;component/Compatibility/Fonts/")).Select(font => font.Source)));
                    Assert.Equal("pack", glyph.FontUri.Scheme);
                    Assert.Contains(name, glyph.FamilyNames.Values);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
    }
}
