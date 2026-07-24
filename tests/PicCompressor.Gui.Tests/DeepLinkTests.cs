using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.Tests;

/// <summary>
/// Versionierter Deep-Link auf den Import (MP-003): nur ein wohlgeformter, unterstützter Link mit
/// mindestens einem Pfad wird angenommen; die Version ist Teil des Kontrakts.
/// </summary>
public sealed class DeepLinkTests
{
    [Fact]
    public void A_versioned_import_link_yields_its_paths()
    {
        var ok = DeepLink.TryParseImport(
            @"piccompressor://v1/import?path=C%3A%5Cpics%5Ca.jpg&path=C%3A%5Cpics%5Cb.png",
            out var paths);

        Assert.True(ok);
        Assert.Equal([@"C:\pics\a.jpg", @"C:\pics\b.png"], paths);
    }

    [Theory]
    [InlineData("piccompressor://v2/import?path=a.jpg")] // unbekannte Version
    [InlineData("piccompressor://v1/open?path=a.jpg")]   // unbekannte Aktion
    [InlineData("piccompressor://v1/import")]            // kein Pfad
    [InlineData("piccompressor://v1/import?path=")]      // leerer Pfad
    [InlineData("https://example.com/import?path=a.jpg")] // fremdes Schema
    [InlineData("C:\\pics\\a.jpg")]                       // gewöhnlicher Pfad, kein Link
    [InlineData("")]
    public void Malformed_or_unsupported_links_are_rejected(string argument)
    {
        Assert.False(DeepLink.TryParseImport(argument, out var paths));
        Assert.Empty(paths);
    }

    [Fact]
    public void The_scheme_is_case_insensitive()
    {
        Assert.True(DeepLink.TryParseImport("PicCompressor://v1/import?path=a.jpg", out _));
    }
}
