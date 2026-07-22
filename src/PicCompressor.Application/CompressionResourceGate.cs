namespace PicCompressor.Application;

/// <summary>
/// FIFO-Ressourcengatter für die Batchausführung (Abschnitt 10.1, O-005). Lässt einen Job erst
/// zu, wenn ein CPU-Slot frei ist und – für Guetzli – zusätzlich ein Guetzli-Slot und das
/// geschätzte Speicherbudget verfügbar sind. Ein Guetzli-Job, dessen Schätzung das gesamte Budget
/// übersteigt, läuft allein, statt dauerhaft zu warten. Strikt FIFO: nur der Kopf der Warteschlange
/// wird zugelassen, damit kein Job aushungert.
/// </summary>
internal sealed class CompressionResourceGate(CompressionResourceLimits limits)
{
    private readonly Lock sync = new();
    private readonly LinkedList<Waiter> waiters = new();
    private int cpuInUse;
    private int guetzliInUse;
    private long guetzliMemoryInUse;

    private sealed record Waiter(bool Guetzli, long Memory, TaskCompletionSource Ready);

    public ValueTask AcquireAsync(bool guetzli, long memory, CancellationToken cancellationToken)
    {
        Waiter waiter;
        lock (sync)
        {
            if (waiters.Count == 0 && CanAdmit(guetzli, memory))
            {
                Reserve(guetzli, memory);
                return ValueTask.CompletedTask;
            }

            waiter = new Waiter(
                guetzli,
                memory,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            waiters.AddLast(waiter);
        }

        return WaitAsync(waiter, cancellationToken);
    }

    public void Release(bool guetzli, long memory)
    {
        lock (sync)
        {
            cpuInUse--;
            if (guetzli)
            {
                guetzliInUse--;
                guetzliMemoryInUse -= memory;
            }

            Promote();
        }
    }

    private async ValueTask WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        await using var registration = cancellationToken.Register(() =>
        {
            lock (sync)
            {
                // Nur stornieren, solange der Job noch wartet; ein bereits zugelassener Job hält
                // seine Reservierung und gibt regulär frei.
                if (waiters.Remove(waiter))
                {
                    waiter.Ready.TrySetCanceled(cancellationToken);
                }
            }
        }).ConfigureAwait(false);

        await waiter.Ready.Task.ConfigureAwait(false);
    }

    private void Promote()
    {
        while (waiters.First is { Value: var next } && CanAdmit(next.Guetzli, next.Memory))
        {
            waiters.RemoveFirst();
            Reserve(next.Guetzli, next.Memory);
            next.Ready.TrySetResult();
        }
    }

    private bool CanAdmit(bool guetzli, long memory)
    {
        if (cpuInUse >= limits.MaxParallelism)
        {
            return false;
        }

        if (!guetzli)
        {
            return true;
        }

        if (guetzliInUse >= limits.MaxGuetzliParallelism)
        {
            return false;
        }

        // Ins Budget passend, oder – bei Übergröße – nur wenn gerade kein Guetzli-Job läuft.
        return guetzliMemoryInUse + memory <= limits.GuetzliMemoryBudgetBytes || guetzliInUse == 0;
    }

    private void Reserve(bool guetzli, long memory)
    {
        cpuInUse++;
        if (guetzli)
        {
            guetzliInUse++;
            guetzliMemoryInUse += memory;
        }
    }
}
