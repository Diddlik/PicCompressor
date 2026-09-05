using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

public sealed class ProfilesViewModelTests
{
    [Fact]
    public void Save_reload_apply_and_delete_preserve_unrelated_settings()
    {
        var store = new InMemoryCompressionProfileStore();
        var source = new SettingsViewModel(profileStore: store)
        {
            Quality = 73, ChromaSubsampling = JpegliChromaSubsampling.Subsampling444,
            AlphaBackground = new(12, 34, 56), MinimumSavingsPercent = 5
        };
        source.Profiles.Name = "Blog";
        source.Profiles.SaveCommand.Execute(null);

        var target = new SettingsViewModel(profileStore: store) { HistoryRetentionDays = 123 };
        var theme = target.Appearance.Theme;
        target.UsesOverwriteOriginal = true;
        target.Profiles.Selected = Assert.Single(target.Profiles.Items);
        target.Profiles.ApplyCommand.Execute(null);
        Assert.Equal(73, target.Quality);
        Assert.Equal(source.AlphaBackground, target.AlphaBackground);
        Assert.Equal(source.ChromaSubsampling, target.ChromaSubsampling);
        Assert.Equal(5, target.MinimumSavingsPercent);
        Assert.Equal(123, target.HistoryRetentionDays);
        Assert.Equal(theme, target.Appearance.Theme);
        Assert.False(target.UsesOverwriteOriginal);
        target.Profiles.DeleteCommand.Execute(null);
        Assert.Empty(store.Load());
        Assert.Equal(73, target.Quality);
    }

    [Fact]
    public void Overwrite_original_round_trips_and_duplicate_requires_selection()
    {
        var store = new InMemoryCompressionProfileStore();
        var settings = new SettingsViewModel(profileStore: store) { UsesOverwriteOriginal = true };
        settings.Profiles.Name = "Blog";
        settings.Profiles.SaveCommand.Execute(null);
        Assert.True(Assert.Single(store.Load()).OverwriteOriginal);
        var reloaded = new SettingsViewModel(profileStore: store);
        reloaded.Profiles.Selected = Assert.Single(reloaded.Profiles.Items);
        Assert.True(reloaded.Profiles.SelectedReplacesOriginals);
        Assert.False(reloaded.UsesOverwriteOriginal);
        reloaded.Profiles.ApplyCommand.Execute(null);
        Assert.True(reloaded.UsesOverwriteOriginal);
        Assert.Equal(CollisionPolicy.Overwrite, reloaded.CollisionPolicy);
        settings.Profiles.Selected = null;
        settings.Profiles.Name = "blog";
        settings.Quality = 50;
        settings.Profiles.SaveCommand.Execute(null);
        Assert.Equal(90, Assert.Single(store.Load()).Quality);
        settings.Profiles.Selected = settings.Profiles.Items[0];
        settings.Profiles.SaveCommand.Execute(null);
        Assert.Equal(50, Assert.Single(store.Load()).Quality);
    }

    [Fact]
    public void Failed_save_does_not_claim_success_or_change_the_collection()
    {
        var settings = new SettingsViewModel(profileStore: new FailingStore());
        settings.Profiles.Name = "Blog";
        settings.Profiles.SaveCommand.Execute(null);
        Assert.Empty(settings.Profiles.Items);
        Assert.NotNull(settings.Profiles.Status);
    }

    private sealed class FailingStore : ICompressionProfileStore
    {
        public IReadOnlyList<CompressionProfile> Load() => [];
        public void Save(IReadOnlyList<CompressionProfile> profiles) => throw new IOException("Read only");
    }
}
