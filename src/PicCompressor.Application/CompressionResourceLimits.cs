namespace PicCompressor.Application;

/// <summary>
/// Ressourcengrenzen der Batchausführung (Abschnitt 10.1, O-005). CPU wird über die
/// Gesamtparallelität begrenzt. Guetzli-Jobs haben zusätzlich eine eigene Parallelitätsgrenze
/// und ein nach Pixelzahl gewichtetes Speicherbudget, damit große Guetzli-Jobs den Rechner nicht
/// unbenutzbar machen. Jpegli-Jobs unterliegen nur der CPU-Grenze.
/// </summary>
public sealed record CompressionResourceLimits
{
    /// <summary>
    /// Konservative Schätzung des Guetzli-Spitzenspeichers je Pixel. Vorgabewert; die genaue Zahl
    /// je Runtime bleibt eine Benchmark-Aufgabe (O-005).
    /// </summary>
    public const long DefaultGuetzliBytesPerPixel = 300;

    public CompressionResourceLimits(
        int maxParallelism,
        int maxGuetzliParallelism,
        long guetzliMemoryBudgetBytes,
        long guetzliBytesPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGuetzliParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxGuetzliParallelism, maxParallelism);
        ArgumentOutOfRangeException.ThrowIfLessThan(guetzliMemoryBudgetBytes, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(guetzliBytesPerPixel);

        MaxParallelism = maxParallelism;
        MaxGuetzliParallelism = maxGuetzliParallelism;
        GuetzliMemoryBudgetBytes = guetzliMemoryBudgetBytes;
        GuetzliBytesPerPixel = guetzliBytesPerPixel;
    }

    /// <summary>Höchste Zahl gleichzeitig laufender Jobs jeder Engine (CPU).</summary>
    public int MaxParallelism { get; }

    /// <summary>Höchste Zahl gleichzeitig laufender Guetzli-Jobs.</summary>
    public int MaxGuetzliParallelism { get; }

    /// <summary>Gesamtes Speicherbudget für gleichzeitig laufende Guetzli-Jobs in Bytes.</summary>
    public long GuetzliMemoryBudgetBytes { get; }

    /// <summary>Gewicht des Guetzli-Speicherbedarfs je Pixel (Abschnitt 10.1).</summary>
    public long GuetzliBytesPerPixel { get; }

    /// <summary>
    /// Reine CPU-Grenze ohne Guetzli-Sonderbudget; Verhalten wie eine einfache Parallelitätsgrenze.
    /// </summary>
    public static CompressionResourceLimits CpuOnly(int maxParallelism) =>
        new(maxParallelism, maxParallelism, long.MaxValue, 0);

    /// <summary>
    /// Konservative Vorgabe: Guetzli erhält höchstens die Hälfte der CPU-Slots und die Hälfte des
    /// verfügbaren Speichers als Budget; die andere Hälfte bleibt für das übrige System frei.
    /// </summary>
    public static CompressionResourceLimits Default(int maxParallelism, long availableMemoryBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(availableMemoryBytes, 1);
        return new(
            maxParallelism,
            Math.Max(1, maxParallelism / 2),
            Math.Max(1, availableMemoryBytes / 2),
            DefaultGuetzliBytesPerPixel);
    }
}
