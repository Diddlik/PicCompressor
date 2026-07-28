using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class IncrementalScanTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static CompressionBatchSettings Settings(int quality = 80, string suffix = "_compressed") =>
        new(
            new JpegliSettings(quality, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            "out",
            suffix);

    private static DiscoveredInput Input(string path, DateTimeOffset? modified = null) =>
        new(path, "", 10, modified ?? Now.AddHours(-1));

    private static ScanStateEntry Entry(DiscoveredInput input, string fingerprint, string output) =>
        new(input.Path, input.FileSizeBytes, input.LastWriteTimeUtc.UtcTicks, fingerprint, output);

    [Fact]
    public void Fingerprint_changes_with_every_effective_setting()
    {
        var baseline = IncrementalScan.Fingerprint(Settings());

        Assert.Equal(baseline, IncrementalScan.Fingerprint(Settings()));
        Assert.NotEqual(baseline, IncrementalScan.Fingerprint(Settings(quality: 70)));
        Assert.NotEqual(baseline, IncrementalScan.Fingerprint(Settings(suffix: "_small")));
    }

    [Fact]
    public void Select_treats_an_unchanged_input_with_existing_output_as_a_no_op()
    {
        var input = Input(Path.GetFullPath("a.png"));
        var fingerprint = IncrementalScan.Fingerprint(Settings());
        var state = new[] { Entry(input, fingerprint, Path.GetFullPath("out/a.jpg")) };

        var selection = IncrementalScan.Select(
            [input],
            state,
            fingerprint,
            TimeSpan.Zero,
            Now,
            new StubFileSystem(Path.GetFullPath("out/a.jpg")),
            StringComparer.OrdinalIgnoreCase);

        Assert.Empty(selection.Inputs);
        Assert.Single(selection.Unchanged);
    }

    [Fact]
    public void Select_processes_again_when_the_recorded_output_is_gone()
    {
        var input = Input(Path.GetFullPath("a.png"));
        var fingerprint = IncrementalScan.Fingerprint(Settings());
        var state = new[] { Entry(input, fingerprint, Path.GetFullPath("out/a.jpg")) };

        var selection = IncrementalScan.Select(
            [input],
            state,
            fingerprint,
            TimeSpan.Zero,
            Now,
            new StubFileSystem(),
            StringComparer.OrdinalIgnoreCase);

        Assert.Single(selection.Inputs);
        Assert.Empty(selection.Unchanged);
    }

    [Fact]
    public void Select_keeps_a_success_without_output_as_a_no_op()
    {
        var input = Input(Path.GetFullPath("a.png"));
        var fingerprint = IncrementalScan.Fingerprint(Settings());
        var state = new[] { Entry(input, fingerprint, "") };

        var selection = IncrementalScan.Select(
            [input],
            state,
            fingerprint,
            TimeSpan.Zero,
            Now,
            new StubFileSystem(),
            StringComparer.OrdinalIgnoreCase);

        Assert.Empty(selection.Inputs);
        Assert.Single(selection.Unchanged);
    }

    [Fact]
    public void Select_processes_again_after_a_changed_input_or_setting()
    {
        var input = Input(Path.GetFullPath("a.png"));
        var fingerprint = IncrementalScan.Fingerprint(Settings());
        var output = Path.GetFullPath("out/a.jpg");
        var fileSystem = new StubFileSystem(output);

        var changedInput = IncrementalScan.Select(
            [input with { FileSizeBytes = 11 }],
            [Entry(input, fingerprint, output)],
            fingerprint,
            TimeSpan.Zero,
            Now,
            fileSystem,
            StringComparer.OrdinalIgnoreCase);
        var changedSettings = IncrementalScan.Select(
            [input],
            [Entry(input, fingerprint, output)],
            IncrementalScan.Fingerprint(Settings(quality: 70)),
            TimeSpan.Zero,
            Now,
            fileSystem,
            StringComparer.OrdinalIgnoreCase);

        Assert.Single(changedInput.Inputs);
        Assert.Single(changedSettings.Inputs);
    }

    [Fact]
    public void Select_leaves_an_input_that_is_still_changing()
    {
        var input = Input(Path.GetFullPath("a.png"), Now.AddSeconds(-5));

        var selection = IncrementalScan.Select(
            [input],
            [],
            IncrementalScan.Fingerprint(Settings()),
            TimeSpan.FromSeconds(30),
            Now,
            new StubFileSystem(),
            StringComparer.OrdinalIgnoreCase);

        Assert.Empty(selection.Inputs);
        Assert.Equal(1, selection.UnstableCount);
    }

    [Fact]
    public void Merge_keeps_entries_outside_this_scan_and_drops_deleted_inputs()
    {
        var scanned = Input(Path.GetFullPath("a.png"));
        var elsewhere = Input(Path.GetFullPath("b.png"));
        var deleted = Input(Path.GetFullPath("c.png"));
        var fingerprint = IncrementalScan.Fingerprint(Settings());
        var current = new[] { Entry(scanned, fingerprint, Path.GetFullPath("out/a.jpg")) };
        var previous = new[]
        {
            Entry(scanned, "stale", Path.GetFullPath("out/a.jpg")),
            Entry(elsewhere, fingerprint, Path.GetFullPath("out/b.jpg")),
            Entry(deleted, fingerprint, Path.GetFullPath("out/c.jpg"))
        };

        var merged = IncrementalScan.Merge(
            previous,
            [scanned],
            current,
            new StubFileSystem(elsewhere.Path),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            [scanned.Path, elsewhere.Path],
            merged.Select(entry => entry.InputPath));
        Assert.Equal(fingerprint, merged[0].SettingsFingerprint);
    }

    private sealed class StubFileSystem(params string[] existingFiles) : IFileSystem
    {
        private readonly HashSet<string> existing = new(
            existingFiles.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);

        public string GetCanonicalPath(string path) => Path.GetFullPath(path);

        public bool FileExists(string path) => existing.Contains(path);

        public bool PathsEqual(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);
    }
}
