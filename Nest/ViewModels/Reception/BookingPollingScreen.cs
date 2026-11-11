using Caliburn.Micro;
using Nest.Synchronization;
using System.Collections.ObjectModel;

namespace Nest.ViewModels.Reception;

// Screen-side lifecycle only; the process-wide coordinator owns all scheduling.
public abstract class BookingPollingScreen : Screen
{
    private readonly Func<PollingCoordinator> coordinator;
    private PollingCoordinator.Subscription? subscription;
    private PollingSnapshot? pending;
    private int pauses;
    private long session;
    protected bool PollingActive { get; private set; }
    protected virtual bool PollingVisible => true;
    private string synchronizationStatus = "Daten werden geladen …";
    public string SynchronizationStatus
    {
        get => synchronizationStatus;
        private set { synchronizationStatus = value; NotifyOfPropertyChange(() => SynchronizationStatus); }
    }

    protected BookingPollingScreen(Func<PollingCoordinator> coordinator) => this.coordinator = coordinator;
    protected override async Task OnActivatedAsync(CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(cancellationToken);
        PollingActive = true;
        SynchronizationStatus = "Daten werden aktualisiert …";
        UpdatePollingSubscription();
    }
    protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
    {
        PollingActive = false;
        RemoveSubscription();
        await base.OnDeactivateAsync(close, cancellationToken);
    }
    protected void UpdatePollingSubscription()
    {
        if (System.Windows.Application.Current?.Dispatcher.HasShutdownStarted == true) { RemoveSubscription(); return; }
        if (!PollingActive || !PollingVisible || pauses != 0) { RemoveSubscription(); return; }
        if (subscription != null) return;
        var currentSession = ++session;
        subscription = coordinator().Subscribe(snapshot =>
        {
            if (currentSession == session && PollingActive && pauses == 0 && PollingVisible) pending = snapshot;
        }, status =>
        {
            if (currentSession != session || !PollingActive || pauses != 0 || !PollingVisible) return;
            // Coordinator retains its last good data on errors. Do not apply that
            // cached snapshot as a fresh result after a command or account switch.
            if (status.State == PollingConnectionState.Online && pending != null)
            {
                var snapshot = pending;
                pending = null;
                ApplyPollingSnapshot(snapshot);
            }
            SynchronizationStatus = status.State == PollingConnectionState.Online
                ? "Daten aktuell · letzte Prüfung " + status.LastSuccess?.ToLocalTime().ToString("HH:mm:ss")
                : "Keine Datenbankverbindung · angezeigte Daten sind möglicherweise veraltet. Automatische Wiederverbindung läuft.";
        });
    }
    private void RemoveSubscription()
    {
        session++;
        pending = null;
        subscription?.Dispose();
        subscription = null;
    }
    protected void RequestPollingRefresh()
    { if (PollingActive && System.Windows.Application.Current?.Dispatcher.HasShutdownStarted != true) coordinator().RequestRefresh(); }

    protected IDisposable PausePolling()
    {
        pauses++;
        RemoveSubscription();
        return new Scope(() => { pauses--; UpdatePollingSubscription(); });
    }
    protected abstract void ApplyPollingSnapshot(PollingSnapshot snapshot);

    // Retain collection/view identity: WPF sort descriptions survive updates.
    protected static void Reconcile<T, TKey>(ObservableCollection<T> collection, IReadOnlyList<T> incoming,
        Func<T, TKey> key, Func<T, T, bool> equal) where TKey : notnull
    {
        var wanted = incoming.Select(key).ToHashSet();
        for (var i = collection.Count - 1; i >= 0; i--)
            if (!wanted.Contains(key(collection[i]))) collection.RemoveAt(i);
        for (var index = 0; index < incoming.Count; index++)
        {
            var row = incoming[index];
            var existing = -1;
            for (var i = index; i < collection.Count; i++)
                if (EqualityComparer<TKey>.Default.Equals(key(collection[i]), key(row))) { existing = i; break; }
            if (existing < 0) collection.Insert(index, row);
            else
            {
                if (existing != index) collection.Move(existing, index);
                if (!equal(collection[index], row)) collection[index] = row;
            }
        }
    }
    private sealed class Scope(System.Action finish) : IDisposable
    {
        private System.Action? action = finish;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
