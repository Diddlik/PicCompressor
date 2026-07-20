using System.Collections.ObjectModel;

namespace PicCompressor.Gui.ViewModels;

/// <summary>
/// Vorher-Nachher-Vergleich. Kandidaten sind ausschließlich Jobs mit validierter, veröffentlichter
/// Ausgabe (Abschnitt 11). Die speicherschonende Vorschauerzeugung folgt mit der Bildschicht;
/// bis dahin zeigt die Ansicht die Kennzahlen und keine erfundene Vorschau.
/// </summary>
public sealed class CompareViewModel : ObservableObject
{
    private QueueItemViewModel? selected;
    private double dividerFraction = 0.5;

    public ObservableCollection<QueueItemViewModel> Candidates { get; } = [];

    public QueueItemViewModel? Selected
    {
        get => selected;
        set
        {
            if (SetProperty(ref selected, value))
            {
                Raise(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>Position des Vergleichsreglers, 0..1.</summary>
    public double DividerFraction
    {
        get => dividerFraction;
        set => SetProperty(ref dividerFraction, Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Übernimmt einen abgeschlossenen Job nur, wenn seine Ausgabe validiert und veröffentlicht
    /// wurde. Alles andere ist nicht vergleichbar.
    /// </summary>
    public void Offer(QueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanCompare || Candidates.Contains(item))
        {
            return;
        }

        Candidates.Insert(0, item);
        Selected ??= item;
        Raise(nameof(HasCandidates));
    }

    public void Remove(QueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Candidates.Remove(item))
        {
            return;
        }

        if (ReferenceEquals(Selected, item))
        {
            Selected = Candidates.FirstOrDefault();
        }

        Raise(nameof(HasCandidates));
    }
}
