using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Views;

public partial class CompareView : UserControl
{
    /// <summary>Schwenkschritt je Pfeiltastendruck in Bildpunkten.</summary>
    private const double KeyboardPanStep = 24;

    // Ein einziges Transformationsobjekt für beide Seiten: die Ansicht kann dadurch gar nicht
    // auseinanderlaufen. Als RenderTransform erbt es keinen DataContext, deshalb wird es hier
    // gesetzt statt in XAML gebunden.
    private readonly ScaleTransform scale = new(1, 1);
    private readonly TranslateTransform translate = new();

    private CompareViewModel? boundViewModel;
    private Point? dragOrigin;

    public CompareView()
    {
        InitializeComponent();

        var transform = new TransformGroup { Children = { scale, translate } };
        CompressedImage.RenderTransform = transform;
        OriginalImage.RenderTransform = transform;

        CompareSurface.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                // Der Maßstab hängt an der Flächengröße; nur die Ansicht kennt sie.
                boundViewModel?.ApplyViewport(
                    CompareSurface.Bounds.Width,
                    CompareSurface.Bounds.Height);
                UpdateDivider();
            }
        };

        CompareSurface.PointerPressed += (_, e) =>
        {
            dragOrigin = e.GetPosition(CompareSurface);
            CompareSurface.Focus();
        };
        CompareSurface.PointerMoved += (_, e) =>
        {
            if (dragOrigin is not Point origin || boundViewModel is null)
            {
                return;
            }

            var current = e.GetPosition(CompareSurface);
            boundViewModel.Pan(current.X - origin.X, current.Y - origin.Y);
            dragOrigin = current;
        };
        CompareSurface.PointerReleased += (_, _) => dragOrigin = null;
        CompareSurface.PointerCaptureLost += (_, _) => dragOrigin = null;
        CompareSurface.PointerWheelChanged += (_, e) =>
        {
            if (boundViewModel is null)
            {
                return;
            }

            boundViewModel.Scale *= e.Delta.Y > 0 ? 1.25 : 1 / 1.25;
            e.Handled = true;
        };

        // Zoom und Schwenk bleiben ohne Zeigegerät erreichbar (Abschnitt 11).
        CompareSurface.KeyDown += OnSurfaceKeyDown;

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

            boundViewModel?.ApplyViewport(
                CompareSurface.Bounds.Width,
                CompareSurface.Bounds.Height);
            UpdateDivider();
            UpdateTransform();
        };
    }

    private void OnSurfaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (boundViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                boundViewModel.Pan(KeyboardPanStep, 0);
                break;
            case Key.Right:
                boundViewModel.Pan(-KeyboardPanStep, 0);
                break;
            case Key.Up:
                boundViewModel.Pan(0, KeyboardPanStep);
                break;
            case Key.Down:
                boundViewModel.Pan(0, -KeyboardPanStep);
                break;
            case Key.Add or Key.OemPlus:
                boundViewModel.ZoomInCommand.Execute(null);
                break;
            case Key.Subtract or Key.OemMinus:
                boundViewModel.ZoomOutCommand.Execute(null);
                break;
            case Key.D0 or Key.NumPad0:
                boundViewModel.ResetViewCommand.Execute(null);
                break;
            case Key.D1 or Key.NumPad1:
                boundViewModel.ActualSizeCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CompareViewModel.DividerFraction):
                UpdateDivider();
                break;
            case nameof(CompareViewModel.RenderScale):
            case nameof(CompareViewModel.PanX):
            case nameof(CompareViewModel.PanY):
                UpdateTransform();
                break;
        }
    }

    private void UpdateTransform()
    {
        if (boundViewModel is null)
        {
            return;
        }

        scale.ScaleX = boundViewModel.RenderScale;
        scale.ScaleY = boundViewModel.RenderScale;
        translate.X = boundViewModel.PanX;
        translate.Y = boundViewModel.PanY;
    }

    /// <summary>
    /// Der Regler beschneidet die obere Schicht, statt sie schmaler zu machen. Eine schmalere
    /// Fläche würde das Bild darin neu einpassen — beide Seiten zeigten dann dieselbe Stelle in
    /// unterschiedlicher Größe und Lage, also zwei Bilder nebeneinander statt eines Vergleichs.
    /// </summary>
    private void UpdateDivider()
    {
        if (boundViewModel is null)
        {
            return;
        }

        var bounds = CompareSurface.Bounds;
        var divider = bounds.Width * boundViewModel.DividerFraction;
        OriginalPane.Clip = new RectangleGeometry(new Rect(0, 0, divider, bounds.Height));

        // Die Linie sitzt mittig auf der Kante und bleibt an den Rändern vollständig sichtbar.
        var left = Math.Clamp(
            divider - (DividerLine.Width / 2),
            0,
            Math.Max(0, bounds.Width - DividerLine.Width));
        DividerLine.Margin = new Thickness(left, 0, 0, 0);
    }
}
