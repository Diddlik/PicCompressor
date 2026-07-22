using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

public sealed class AboutViewModelTests
{
    [Fact]
    public void Version_is_never_empty()
    {
        var about = new AboutViewModel();

        Assert.False(string.IsNullOrWhiteSpace(about.Version));
        // Buildmetadaten hinter '+' (Git-Hash) gehören nicht in die Anzeige.
        Assert.DoesNotContain('+', about.Version);
        Assert.Contains(about.Version, about.VersionLabel);
    }

    [Fact]
    public void Credits_list_the_bundled_native_and_managed_components()
    {
        var about = new AboutViewModel();

        Assert.NotEmpty(about.Components);
        // Die nativen Encoder und die UI-Basis müssen genannt sein.
        Assert.Contains(about.Components, entry => entry.Name == "Jpegli");
        Assert.Contains(about.Components, entry => entry.Name == "Guetzli");
        Assert.Contains(about.Components, entry => entry.Name == "Avalonia");
        Assert.All(about.Components, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.License));
        });
    }
}
