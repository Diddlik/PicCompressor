using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class OutputPathPlannerTests
{
    [Fact]
    public void Plan_reports_existing_target_for_skip()
    {
        var fileSystem = new StubFileSystem("image.jpg");

        var exception = Assert.Throws<JobCreationException>(
            () => new OutputPathPlanner(fileSystem).Plan(
                fileSystem.Canonical("image.jpg"),
                CollisionPolicy.Skip));

        Assert.Equal(CompressionErrorCategory.OutputConflict, exception.Category);
    }

    [Fact]
    public void Plan_renames_past_existing_and_reserved_targets()
    {
        var fileSystem = new StubFileSystem("image.jpg", "image_1.jpg");
        var reserved = new[] { fileSystem.Canonical("image_2.jpg") };

        var result = new OutputPathPlanner(fileSystem).Plan(
            fileSystem.Canonical("image.jpg"),
            CollisionPolicy.Rename,
            reserved);

        Assert.Equal(fileSystem.Canonical("image_3.jpg"), result);
    }

    [Fact]
    public void Plan_allows_overwriting_existing_target()
    {
        var fileSystem = new StubFileSystem("image.jpg");
        var desired = fileSystem.Canonical("image.jpg");

        var result = new OutputPathPlanner(fileSystem).Plan(
            desired,
            CollisionPolicy.Overwrite);

        Assert.Equal(desired, result);
    }

    [Fact]
    public void Plan_rejects_overwriting_target_reserved_by_another_job()
    {
        var fileSystem = new StubFileSystem();
        var desired = fileSystem.Canonical("image.jpg");

        var exception = Assert.Throws<JobCreationException>(
            () => new OutputPathPlanner(fileSystem).Plan(
                desired,
                CollisionPolicy.Overwrite,
                [desired]));

        Assert.Equal(CompressionErrorCategory.OutputConflict, exception.Category);
    }

    private sealed class StubFileSystem(params string[] existingFiles) : IFileSystem
    {
        private readonly HashSet<string> existing = new(
            existingFiles.Select(path => Path.GetFullPath(path)),
            StringComparer.OrdinalIgnoreCase);

        public string Canonical(string path) => Path.GetFullPath(path);

        public string GetCanonicalPath(string path) => Canonical(path);

        public bool FileExists(string path) => existing.Contains(path);

        public bool PathsEqual(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);
    }
}
