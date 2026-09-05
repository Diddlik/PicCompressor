using System.Text.Json;
using System.Text.Json.Serialization;
using PicCompressor.Application;

namespace PicCompressor.Infrastructure;

public sealed class JsonCompressionProfileStore(string path) : ICompressionProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        WriteIndented = true
    };

    private readonly string path = Path.GetFullPath(path);

    public IReadOnlyList<CompressionProfile> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var document = JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(path), Options);
        if (document is null || document.SchemaVersion != 1 || document.Profiles is null)
        {
            throw new InvalidDataException("Unsupported or invalid profile document.");
        }

        Validate(document.Profiles);
        return document.Profiles;
    }

    public void Save(IReadOnlyList<CompressionProfile> profiles)
    {
        Validate(profiles);
        // Refuse to replace an unreadable or newer document with a fresh collection.
        _ = Load();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new ProfileDocument(1, profiles), Options));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Validate(IReadOnlyList<CompressionProfile> profiles)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                throw new InvalidDataException("A profile cannot be null.");
            }

            profile.Validate();
            if (!names.Add(profile.Name))
            {
                throw new InvalidDataException("Duplicate profile name.");
            }
        }
    }

    private sealed record ProfileDocument(int SchemaVersion, IReadOnlyList<CompressionProfile> Profiles);
}
