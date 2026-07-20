using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Views;

public partial class CompareView : UserControl
{
    private CompareViewModel? boundViewModel;

    public CompareView()
    {
        InitializeComponent();

        CompareSurface.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                UpdateDivider();
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (boundViewModel is not null)
            {
                boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            boundViewModel = DataContext as CompareViewModel;
            if (boundViewModel is not null)
            {
                boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            UpdateDivider();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CompareViewModel.DividerFraction))
        {
            UpdateDivider();
        }
    }

    /// <summary>Breite der Originalfläche folgt dem Regler; beide Seiten bleiben beschriftet.</summary>
    private void UpdateDivider()
    {
        if (boundViewModel is not null)
        {
            OriginalPane.Width = CompareSurface.Bounds.Width * boundViewModel.DividerFraction;
        }
    }
}
