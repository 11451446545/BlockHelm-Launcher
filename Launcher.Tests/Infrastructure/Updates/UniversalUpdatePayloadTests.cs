using System.Security.Cryptography;
using Launcher.Infrastructure.Updates;
using Launcher.Tests.Helpers;
using Xunit;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class UniversalUpdatePayloadTests : TestTempDirectory
{
    [Theory]
    [InlineData(6, 1, "win7")]
    [InlineData(6, 2, "win7")]
    [InlineData(6, 3, "win7")]
    [InlineData(10, 0, "modern")]
    public void ChoosesRuntimeForExistingWindowsInstallations(int major, int minor, string variant) =>
        Assert.Equal($"BlockHelm.Update.{variant}.exe", UniversalUpdatePayload.SelectResource(new Version(major, minor)));

    [Fact]
    public void RejectsUnsupportedWindows() =>
        Assert.Throws<PlatformNotSupportedException>(() => UniversalUpdatePayload.SelectResource(new Version(6, 0)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExtractionVerifiesContentBeforeReturningAReplacement(int corruption)
    {
        Directory.CreateDirectory(TempRoot);
        var data = new byte[] { 77, 90, 1, 2, 3 };
        var payload = new UniversalUpdatePayload("BlockHelm.Update.modern.exe", data.Length, Convert.ToHexString(SHA256.HashData(data)));
        var bytes = corruption switch
        {
            -1 => data[..^1],
            1 => data.Concat(new byte[] { 4 }).ToArray(),
            2 => new byte[] { 77, 90, 1, 2, 9 },
            _ => data
        };
        var preserved = Path.Combine(TempRoot, "existing-settings.json");
        File.WriteAllText(preserved, "keep");
        using var source = new MemoryStream(bytes);
        if (corruption == 0)
        {
            var path = payload.ExtractVerified(source, TempRoot);
            Assert.Equal(data, File.ReadAllBytes(path));
            Assert.StartsWith(TempRoot + Path.DirectorySeparatorChar, path);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => payload.ExtractVerified(source, TempRoot));
            Assert.Empty(Directory.GetDirectories(TempRoot));
        }
        Assert.Equal("keep", File.ReadAllText(preserved));
    }
}
