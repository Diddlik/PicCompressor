using System.Collections.ObjectModel;
using System.Text.Json;
using PicCompressor.Application;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.ViewModels;

public sealed class ProfilesViewModel : ObservableObject
{
    private readonly SettingsViewModel settings;
    private readonly ICompressionProfileStore store;
    private CompressionProfile? selected;
    private string name = "";
    private string? statusKey;
    private bool loadFailed;

    public ProfilesViewModel(SettingsViewModel settings, ICompressionProfileStore store)
    {
        this.settings = settings;
        this.store = store;
        SaveCommand = new RelayCommand(Save, () => !loadFailed);
        ApplyCommand = new RelayCommand(Apply, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => !loadFailed && Selected is not null);
        try
        {
            foreach (var profile in store.Load())
            {
                Items.Add(profile);
            }
        }
        catch (Exception exception) when (IsProfileError(exception))
        {
            loadFailed = true;
            SetStatus("Profile_LoadFailed");
        }
    }

    public ObservableCollection<CompressionProfile> Items { get; } = [];
    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public string? Status => statusKey is null ? null : Localizer.Instance[statusKey];
    public string Name { get => name; set => SetProperty(ref name, value); }

    public CompressionProfile? Selected
    {
        get => selected;
        set
        {
            if (SetProperty(ref selected, value))
            {
                Name = value?.Name ?? "";
                ApplyCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void Save()
    {
        if (settings.UsesOverwriteOriginal)
        {
            SetStatus("Profile_OverwriteExcluded");
            return;
        }

        var profile = new CompressionProfile
        {
            Name = Name.Trim(),
            Quality = settings.Quality,
            ChromaSubsampling = settings.ChromaSubsampling,
            ProgressiveLevel = settings.ProgressiveLevel,
            ExifPolicy = settings.ExifPolicy,
            ColorProfilePolicy = settings.ColorProfilePolicy,
            AlphaBackground = settings.AlphaBackground,
            Suffix = settings.Suffix,
            OutputDirectory = settings.UsesCustomDirectory ? settings.OutputDirectory : null,
            CollisionPolicy = settings.CollisionPolicy,
            LargerOutputPolicy = settings.LargerOutputPolicy,
            ParallelJobs = settings.ParallelJobs,
            JpegliTimeoutSeconds = settings.JpegliTimeoutSeconds,
            MinimumSavingsPercent = settings.MinimumSavingsPercent
        };
        try
        {
            profile.Validate();
            if (settings.UsesCustomDirectory && string.IsNullOrWhiteSpace(profile.OutputDirectory))
            {
                throw new ArgumentException("An output directory is required.");
            }

            var existing = Items.FirstOrDefault(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && existing != Selected)
            {
                SetStatus("Profile_Duplicate");
                return;
            }

            var updated = Items.Where(item => item != existing).Append(profile).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            store.Save(updated);
            Items.Clear();
            foreach (var item in updated) { Items.Add(item); }
            Selected = profile;
            SetStatus("Profile_Saved");
        }
        catch (Exception exception) when (IsProfileError(exception))
        {
            SetStatus("Profile_SaveFailed");
        }
    }

    private void Apply()
    {
        if (Selected is not { } profile) { return; }
        var timeoutChanged = settings.JpegliTimeoutSeconds != profile.JpegliTimeoutSeconds;
        settings.ApplyProfile(profile);
        SetStatus(timeoutChanged ? "Profile_AppliedRestart" : "Profile_Applied");
    }

    private void Delete()
    {
        if (Selected is not { } profile) { return; }
        try
        {
            store.Save(Items.Where(item => item != profile).ToArray());
            Items.Remove(profile);
            Selected = null;
            SetStatus("Profile_Deleted");
        }
        catch (Exception exception) when (IsProfileError(exception))
        {
            SetStatus("Profile_SaveFailed");
        }
    }

    private void SetStatus(string key)
    {
        statusKey = key;
        Raise(nameof(Status));
    }

    private static bool IsProfileError(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException;
}
