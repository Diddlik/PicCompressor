using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Views;

public partial class MainWindow : Window
{
    // Randbreite, in der der Zeiger das Fenster greift (rahmenloses Fenster, eigene Chrome).
    private const double ResizeEdge = 6;

    public MainWindow()
    {
        InitializeComponent();

        // Die Ansicht meldet nur die Breite; die Layoutstufen entscheidet das ViewModel.
        SizeChanged += (_, e) =>
            (DataContext as MainWindowViewModel)?.ApplyWidth(e.NewSize.Width);

        // Esc schließt zuerst den „Über“-Overlay, bevor andere Escape-Bindungen greifen.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // Rahmenloses Fenster (SystemDecorations=None): Größenänderung an den Rändern selbst führen.
        AddHandler(PointerMovedEvent, OnResizeHint, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnResizePressed, RoutingStrategies.Tunnel);

        UpdateChromeRadius();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateChromeRadius();
        }
    }

    // Maximiert sieht ein runder Vollbildrahmen falsch aus; dann flache Ecken.
    private void UpdateChromeRadius() =>
        RootChrome.CornerRadius = new CornerRadius(WindowState == WindowState.Maximized ? 0 : 18);

    private WindowEdge? EdgeAt(Point p)
    {
        if (WindowState == WindowState.Maximized)
        {
            return null;
        }

        var left = p.X <= ResizeEdge;
        var right = p.X >= Bounds.Width - ResizeEdge;
        var top = p.Y <= ResizeEdge;
        var bottom = p.Y >= Bounds.Height - ResizeEdge;

        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (true, _, _, true) => WindowEdge.NorthEast,
            (_, true, true, _) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.North,
            (_, true, _, _) => WindowEdge.South,
            (_, _, true, _) => WindowEdge.West,
            (_, _, _, true) => WindowEdge.East,
            _ => null
        };
    }

    private void OnResizeHint(object? sender, PointerEventArgs e) =>
        Cursor = EdgeAt(e.GetPosition(this)) switch
        {
            WindowEdge.NorthWest or WindowEdge.SouthEast => new Cursor(StandardCursorType.TopLeftCorner),
            WindowEdge.NorthEast or WindowEdge.SouthWest => new Cursor(StandardCursorType.TopRightCorner),
            WindowEdge.North or WindowEdge.South => new Cursor(StandardCursorType.SizeNorthSouth),
            WindowEdge.West or WindowEdge.East => new Cursor(StandardCursorType.SizeWestEast),
            _ => Cursor.Default
        };

    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && EdgeAt(e.GetPosition(this)) is { } edge)
        {
            BeginResizeDrag(edge, e);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel vm && vm.IsAboutOpen)
        {
            vm.IsAboutOpen = false;
            e.Handled = true;
        }
    }

    private void OnAboutBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsAboutOpen = false;
        }
    }

    // Eigene Titelleiste: die Kopfzeile zieht das Fenster, Doppelklick maximiert.
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseWindow(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void OnMinimizeWindow(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeWindow(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        ToggleMaximize();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
