using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PicCompressor.Application;
using PicCompressor.Gui.ViewModels;
using PicCompressor.Gui.Views;

namespace PicCompressor.Gui.ViewTests;

[Collection(AvaloniaCollection.Name)]
public sealed class ProfileViewTests(AvaloniaSession session)
{
    [Fact]
    public Task Profile_controls_save_apply_and_delete_at_minimum_window_size() => session.RunAsync(() =>
    {
        var store = new InMemoryCompressionProfileStore();
        var settings = new SettingsViewModel(profileStore: store) { Quality = 73 };
        var view = new SettingsView { DataContext = settings };
        var window = new Window { Content = view, Width = 960, Height = 620 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var name = view.FindControl<TextBox>("ProfileName")!;
            var save = view.FindControl<Button>("SaveProfileButton")!;
            var apply = view.FindControl<Button>("ApplyProfileButton")!;
            var delete = view.FindControl<Button>("DeleteProfileButton")!;
            Assert.False(apply.IsEffectivelyEnabled);
            Assert.False(delete.IsEffectivelyEnabled);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(name)));

            name.Text = "Blog";
            Dispatcher.UIThread.RunJobs();
            Click(save);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Blog", Assert.Single(store.Load()).Name);
            Assert.True(apply.IsEffectivelyEnabled);
            settings.Quality = 40;
            Click(apply);
            Assert.Equal(73, settings.Quality);

            foreach (var button in new[] { save, apply, delete })
            {
                var position = button.TranslatePoint(default, view)!.Value;
                Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= view.Bounds.Width);
                Assert.True(button.Bounds.Height > 0);
            }

            if (Environment.GetEnvironmentVariable("PICCOMPRESSOR_PROFILE_SCREENSHOT") is { } screenshot)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(960, 620));
                bitmap.Render(window);
                bitmap.Save(screenshot, PngBitmapEncoderOptions.Default);
            }

            Click(delete);
            Assert.Empty(store.Load());

            void Click(Button button)
            {
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Close(); }
    });
}
