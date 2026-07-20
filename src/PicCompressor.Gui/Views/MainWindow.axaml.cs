using Avalonia.Controls;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Die Ansicht meldet nur die Breite; die Layoutstufen entscheidet das ViewModel.
        SizeChanged += (_, e) =>
            (DataContext as MainWindowViewModel)?.ApplyWidth(e.NewSize.Width);
    }
}
