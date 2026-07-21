using Avalonia;
using Avalonia.Headless;
using PicCompressor.Gui;

[assembly: AvaloniaTestApplication(typeof(PicCompressor.Gui.ViewTests.HeadlessTestApplication))]

// Die Avalonia-Sitzung ist prozessweit und thread-gebunden; die Testklassen laufen nacheinander.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PicCompressor.Gui.ViewTests;

/// <summary>
/// Avalonia-Plattform für die Ansichtstests. Die kopflose Ersatzzeichnung wird abgeschaltet,
/// damit Bitmapoperationen wie <c>WriteableBitmap.Lock</c> denselben Weg nehmen wie im
/// laufenden Programm.
/// </summary>
public static class HeadlessTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Hält die Avalonia-Sitzung. Avalonia bindet die Plattform an einen eigenen Dispatcher-Thread;
/// jeder Testkörper läuft deshalb über <see cref="RunAsync"/> auf diesem Thread.
///
/// Diese Tests liegen bewusst in einem eigenen Projekt: eine laufende Avalonia-Plattform macht
/// den Dispatcher-Thread zum UI-Thread und lässt jeden Zugriff aus einem anderen Thread
/// scheitern. Die übrigen GUI-Tests kommen ohne Plattform aus und sollen es bleiben. Das
/// xUnit-Paket von Avalonia setzt zudem xUnit v3 voraus, das Repository bleibt bei xUnit 2 —
/// die Sitzung wird daher direkt gesteuert.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AvaloniaCollection : ICollectionFixture<AvaloniaSession>
{
    public const string Name = "Avalonia";
}

/// <summary>
/// Die Sitzung gilt für die gesamte Assembly. Sie wird über eine Sammlung geteilt, damit sie
/// nicht nach der ersten Testklasse verworfen wird und die folgenden Klassen auf eine bereits
/// beendete Plattform treffen.
/// </summary>
public sealed class AvaloniaSession : IDisposable
{
    private readonly HeadlessUnitTestSession session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AvaloniaSession).Assembly);

    public Task RunAsync(Func<Task> body) =>
        session.Dispatch(
            async () =>
            {
                await body().ConfigureAwait(true);
                return true;
            },
            CancellationToken.None);

    public Task RunAsync(Action body) => session.Dispatch(body, CancellationToken.None);

    public void Dispose() => session.Dispose();
}
