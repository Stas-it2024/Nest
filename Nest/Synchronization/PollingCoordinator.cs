using System.Runtime.CompilerServices;
using System.Threading.Channels;

[assembly: InternalsVisibleTo("Nest.PollingTests")]

namespace Nest.Synchronization;

public enum PollingConnectionState { Waiting, Online, Offline }
public sealed record PollingStatus(PollingConnectionState State, int ConsecutiveFailures, DateTimeOffset? LastSuccess);
internal interface IPollingSource { Task<PollingSnapshot> ReadAsync(CancellationToken cancellationToken); }
internal interface IPollingDispatcher
{
    bool CheckAccess();
    Task InvokeAsync(Action action, CancellationToken cancellationToken);
}

/// <summary>
/// One lazy coordinator per application process, inactive until Subscribe.
/// Future screens dispose subscriptions on deactivation/logout; the application
/// awaits DisposeAsync on shutdown. Deliberately not wired to any screen yet.
/// </summary>
public sealed class PollingCoordinator : IAsyncDisposable
{
    private static readonly Lazy<PollingCoordinator> Instance = new(CreateShared);
    public static PollingCoordinator Shared => Instance.Value;
    private static PollingCoordinator CreateShared()
    {
        var ui = System.Windows.Application.Current?.Dispatcher ??
            throw new InvalidOperationException("Create the shared coordinator after WPF application startup.");
        var coordinator = new PollingCoordinator(new RepositoryPollingSource(), new WpfPollingDispatcher());
        // Invalidate synchronously even when application shutdown cannot await an
        // outstanding synchronous Repository read. No queued result can be applied.
        ui.ShutdownStarted += (_, _) => { _ = coordinator.DisposeAsync(); };
        return coordinator;
    }
    private readonly object gate = new();
    private readonly IPollingSource source;
    private readonly IPollingDispatcher dispatcher;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Subscription> subscriptions = new();
    private CancellationTokenSource? reading;
    private Task? runner;
    private long generation;
    private bool stopped;
    private TimeSpan interval = TimeSpan.FromSeconds(1);
    private PollingSnapshot? snapshot;
    private PollingStatus status = new(PollingConnectionState.Waiting, 0, null);

    internal PollingCoordinator(IPollingSource source, IPollingDispatcher dispatcher,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    { this.source = source; this.dispatcher = dispatcher; this.delay = delay ?? Task.Delay; }

    public TimeSpan Interval
    {
        get { lock (gate) return interval; }
        set
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(value));
            lock (gate) { ThrowIfStopped(); interval = value; signals.Writer.TryWrite(true); }
        }
    }

    // Callbacks run on Dispatcher. Only changed data (or a new subscriber) invokes onData.
    // Consumers remain responsible for preserving forms, filters and selection.
    public Subscription Subscribe(Action<PollingSnapshot> onData, Action<PollingStatus> onStatus)
    {
        ArgumentNullException.ThrowIfNull(onData);
        ArgumentNullException.ThrowIfNull(onStatus);
        if (!dispatcher.CheckAccess()) throw new InvalidOperationException("Subscribe on the UI thread.");
        lock (gate)
        {
            ThrowIfStopped();
            var subscription = new Subscription(this, onData, onStatus);
            subscriptions.Add(subscription);
            Invalidate();
            runner ??= Task.Run(RunAsync);
            return subscription;
        }
    }

    // Request after confirmed local writes; old in-flight/queued reads cannot overwrite them.
    public void RequestRefresh() { lock (gate) { ThrowIfStopped(); Invalidate(); } }
    private void Invalidate() { generation++; reading?.Cancel(); signals.Writer.TryWrite(true); }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                long version;
                CancellationTokenSource? read;
                lock (gate)
                {
                    if (stopped) return;
                    while (signals.Reader.TryRead(out _)) { }
                    version = generation;
                    read = subscriptions.Count == 0 ? null : CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    reading = read;
                }
                if (read != null)
                {
                    try
                    {
                        var candidate = await source.ReadAsync(read.Token).ConfigureAwait(false);
                        read.Token.ThrowIfCancellationRequested();
                        await dispatcher.InvokeAsync(() =>
                        {
                            lock (gate)
                            {
                                if (stopped || version != generation || read.IsCancellationRequested) return;
                                if (snapshot == null || !snapshot.HasSameData(candidate)) snapshot = candidate;
                                status = new(PollingConnectionState.Online, 0, DateTimeOffset.UtcNow);
                                Deliver(version);
                            }
                        }, stopping.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (read.IsCancellationRequested || stopping.IsCancellationRequested) { }
                    catch (Exception)
                    {
                        try
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                lock (gate)
                                {
                                    if (stopped || version != generation) return;
                                    status = new(PollingConnectionState.Offline, Math.Min(30, status.ConsecutiveFailures + 1), status.LastSuccess);
                                    Deliver(version);
                                }
                            }, stopping.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                        catch (Exception) { /* A shutting-down Dispatcher cannot receive notifications. */ }
                    }
                    finally { lock (gate) { reading = null; read.Dispose(); } }
                }
                TimeSpan? wait;
                lock (gate)
                {
                    if (stopped) return;
                    wait = subscriptions.Count == 0 ? null : TimeSpan.FromMilliseconds(
                        Math.Min(60000, interval.TotalMilliseconds * Math.Pow(2, Math.Min(status.ConsecutiveFailures, 6))));
                }
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                var signal = signals.Reader.WaitToReadAsync(waitCancellation.Token).AsTask();
                var timer = wait.HasValue ? delay(wait.Value, waitCancellation.Token) : Task.Delay(Timeout.Infinite, waitCancellation.Token);
                await Task.WhenAny(signal, timer).ConfigureAwait(false);
                waitCancellation.Cancel();
                try { await Task.WhenAll(signal, timer).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                stopping.Token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    private void Deliver(long version)
    {
        foreach (var subscription in subscriptions.ToArray())
        {
            if (stopped || version != generation) return;
            if (!subscriptions.Contains(subscription)) continue;
            try
            {
                if (snapshot != null && !ReferenceEquals(subscription.Delivered, snapshot))
                { subscription.OnData(snapshot); subscription.Delivered = snapshot; }
                if (!stopped && version == generation && subscriptions.Contains(subscription)) subscription.OnStatus(status);
                subscription.DeliveryFailed = false;
            }
            catch (Exception) { subscription.DeliveryFailed = true; }
        }
    }
    private void ThrowIfStopped() => ObjectDisposedException.ThrowIf(stopped, this);
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (!stopped) { stopped = true; generation++; subscriptions.Clear(); stopping.Cancel(); reading?.Cancel(); }
            return new ValueTask(runner ?? Task.CompletedTask);
        }
    }
    public sealed class Subscription : IDisposable
    {
        private readonly PollingCoordinator owner;
        internal readonly Action<PollingSnapshot> OnData;
        internal readonly Action<PollingStatus> OnStatus;
        internal PollingSnapshot? Delivered;
        public bool DeliveryFailed { get; internal set; }
        internal Subscription(PollingCoordinator owner, Action<PollingSnapshot> onData, Action<PollingStatus> onStatus)
        { this.owner = owner; OnData = onData; OnStatus = onStatus; }
        public void Dispose()
        { lock (owner.gate) if (owner.subscriptions.Remove(this)) owner.Invalidate(); }
    }
}
