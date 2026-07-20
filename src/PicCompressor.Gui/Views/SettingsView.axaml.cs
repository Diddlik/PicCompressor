using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        BrowseDirectoryButton.Click += async (_, _) => await BrowseDirectoryAsync();
    }

    private async Task BrowseDirectoryAsync()
    {
        if (DataContext is not SettingsViewModel viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = Localizer.Instance["Set_FolderPickerTitle"] });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            viewModel.OutputDirectory = path;
        }
    }
}
