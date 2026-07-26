using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;
using PicCompressor.Gui.Views;

namespace PicCompressor.Gui.ViewTests;

[Collection(AvaloniaCollection.Name)]
public sealed class UiRefinementTests(AvaloniaSession session)
{
    [Fact]
    public Task Main_window_honors_the_documented_minimum_height() =>
        session.RunAsync(() => Assert.Equal(620, new MainWindow().MinHeight));

    [Fact]
    public Task File_button_matches_the_prototype_elevation() =>
        session.RunAsync(() =>
        {
            var view = new DashboardView
            {
                DataContext = new DashboardViewModel(
                    new SettingsViewModel(),
                    new UnconfiguredCompressionService())
            };
            var window = Show(view);

            var button = view.FindControl<Button>("BrowseButton")!;
            var shell = button.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "shell");

            Assert.Equal(3, shell.BoxShadow[0].OffsetX);
            Assert.Equal(3, shell.BoxShadow[0].OffsetY);

            var buttonOrigin = button.TranslatePoint(default, window)!.Value;
            window.MouseMove(buttonOrigin + new Vector(button.Bounds.Width / 2, button.Bounds.Height / 2));
            Dispatcher.UIThread.RunJobs();

            Assert.True(button.IsPointerOver);
            Assert.Equal(4, shell.BoxShadow[0].OffsetX);
            Assert.Equal(4, shell.BoxShadow[0].OffsetY);

            var actions = view.FindControl<StackPanel>("BrowseActions")!;
            var buttonInRow = button.TranslatePoint(default, actions)!.Value;
            Assert.True(
                buttonInRow.Y + button.Bounds.Height + shell.BoxShadow[0].OffsetY
                <= actions.Bounds.Height,
                $"button y {buttonInRow.Y}, height {button.Bounds.Height}, "
                + $"shadow {shell.BoxShadow[0].OffsetY}, row {actions.Bounds.Height}");
        });

    [Fact]
    public Task Changed_number_keeps_the_rounded_field_without_a_selection_rectangle() =>
        session.RunAsync(() =>
        {
            var view = new SettingsView { DataContext = new SettingsViewModel() };
            var window = Show(view);

            var input = view.FindControl<NumericUpDown>("MinimumSavingsInput")!;
            input.Value = 10;
            var editor = input.GetVisualDescendants().OfType<TextBox>().Single();
            var editorBorder = editor.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "PART_BorderElement");
            editor.BringIntoView();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();
            var editorOrigin = editor.TranslatePoint(default, window)!.Value;

            window.MouseMove(editorOrigin + new Vector(4, editor.Bounds.Height / 2));
            editor.Focus();
            Dispatcher.UIThread.RunJobs();

            var spinner = input.GetVisualDescendants().OfType<ButtonSpinner>().Single();

            Assert.Equal(new CornerRadius(12), spinner.CornerRadius);
            Assert.Equal(Brushes.Transparent, editor.SelectionBrush);
            Assert.True(editor.IsPointerOver);
            Assert.Equal(default, editorBorder.BorderThickness);
            Assert.Equal(Brushes.Transparent, editorBorder.Background);
        });

    [Fact]
    public Task Update_card_and_shadow_are_reachable_at_minimum_height() =>
        session.RunAsync(() =>
        {
            var view = new SettingsView { DataContext = new SettingsViewModel() };
            Show(view, 700, 500);

            var scroll = view.FindControl<ScrollViewer>("SettingsScrollViewer")!;
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            scroll.UpdateLayout();

            var card = view.FindControl<Border>("UpdateCard")!;
            var content = view.FindControl<StackPanel>("SettingsContent")!;
            var shadowBottom = card.TranslatePoint(
                new Point(0, card.Bounds.Height + 6),
                scroll);

            Assert.NotNull(shadowBottom);
            Assert.True(
                shadowBottom.Value.Y <= scroll.Bounds.Height,
                $"shadow bottom {shadowBottom.Value.Y}, viewport {scroll.Bounds.Height}, "
                + $"extent {scroll.Extent.Height}, offset {scroll.Offset.Y}, "
                + $"content {content.Bounds.Height}, card y {card.TranslatePoint(default, content)!.Value.Y}, "
                + $"card height {card.Bounds.Height}");
        });

    [Fact]
    public Task History_header_stays_fixed_while_entries_scroll() =>
        session.RunAsync(() =>
        {
            var history = DesignData.History;
            for (var index = 0; index < 30; index++)
            {
                history.Entries.Add(
                    new HistoryEntryViewModel(
                        new HistoryRecord(
                            DateTimeOffset.Now.AddMinutes(-index),
                            $"history-{index}.jpg",
                            EngineIds.Jpegli,
                            2_000,
                            1_000,
                            JobStatus.Succeeded,
                            null)));
            }

            var view = new HistoryView { DataContext = history };
            Show(view, 900, 500);

            var header = view.FindControl<Grid>("HistoryHeader")!;
            var scroll = view.FindControl<ScrollViewer>("HistoryEntriesScroll")!;
            var headerBefore = header.TranslatePoint(default, view)!.Value;

            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            scroll.UpdateLayout();

            Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(headerBefore, header.TranslatePoint(default, view)!.Value);
        });

    private static Window Show(Control content, double width = 900, double height = 700)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        window.UpdateLayout();
        return window;
    }
}
