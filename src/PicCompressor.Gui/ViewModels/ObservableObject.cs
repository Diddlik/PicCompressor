using System.ComponentModel;
using System.Runtime.CompilerServices;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged, INotifyLanguageChanged
{
    protected ObservableObject() => Localizer.Instance.Register(this);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Nach einem Sprachwechsel gelten alle abgeleiteten Texte als verändert.</summary>
    public void OnLanguageChanged() => Raise(string.Empty);

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
