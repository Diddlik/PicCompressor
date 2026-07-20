using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        AddFilesButton.Click += async (_, _) => await BrowseAsync();
        BrowseButton.Click += async (_, _) => await BrowseAsync();
    }

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var paths = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .OfType<string>()
            .ToList();
        if (paths is { Count: > 0 })
        {
            viewModel.AddPaths(paths);
        }
    }

    private async Task BrowseAsync()
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["Dash_PickerTitle"],
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Instance["Dash_PickerFilter"])
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png"]
                }
            ]
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .ToList();
        if (paths.Count > 0)
        {
            viewModel.AddPaths(paths);
        }
    }
}
