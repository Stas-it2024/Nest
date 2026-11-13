using Nest.Synchronization;
using System.Reflection;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;

internal static partial class Program
{
    private static int assertions;
    private static void Check(bool value)
    { if (!value) throw new InvalidOperationException("Polling invariant failed."); Interlocked.Increment(ref assertions); }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<T> Bounded<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(8));
    private static Task Bounded(Task task) => task.WaitAsync(TimeSpan.FromSeconds(8));
    private static PollingSnapshot Data(string status = "Reserviert") => new(
        new[] { new RoomSnapshot(1, 101, "Standard", 50, 2, "Verfuegbar", null, null, new DateTime(2026, 1, 1), null) },
        new[] { new BookingSnapshot("CODE", "Guest", "101", "Standard", new DateTime(2026, 1, 1), new DateTime(2026, 1, 2), 1, 1, 0, 50, 0, "Bar", status) });

    private static async Task<int> Main()
    {
        var oldMode = Environment.GetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable);
        var oldConnection = Environment.GetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable);
        try
        {
            // Any accidental real Repository call must fail before opening a connection.
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable, "invalid-offline-test");
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable, null);
            await Run().WaitAsync(TimeSpan.FromSeconds(120));
            return 0;
        }
        catch (Exception error)
        {
            // Report a genuine failure/nonzero exit without an unhandled-exception Windows dialog.
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable, oldMode);
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable, oldConnection);
        }
    }
    private static async Task Run()
    {
        using (var ui = new Ui())
        {
        await SnapshotChecks(); Console.WriteLine("PASS snapshot equality/all projected fields");
        await IntervalAndChanges(ui); Console.WriteLine("PASS interval/change detection/UI thread");
        await Errors(ui); Console.WriteLine("PASS failure/backoff/recovery");
        await CoalescingAndStale(ui); Console.WriteLine("PASS coalescing/stale generations");
        await UnsubscribeAndShutdown(ui); Console.WriteLine("PASS unsubscribe/cancellation/shutdown");
        await Consumers(ui); Console.WriteLine("PASS consumer isolation");
        await RealAdapter(ui); Console.WriteLine("PASS production Dispatcher adapter");
        await AdditionalLifecycle(ui); Console.WriteLine("PASS idle/new subscriber/reentrant invalidation");
        await ScreenChecks(ui);
        await RoomChecks(ui);
        await ReservationLayoutChecks(ui);
        await AllBookingsGridChecks(ui);
        await RemainingGridChecks(ui);
        Check(ReferenceEquals(PollingCoordinator.Shared, PollingCoordinator.Shared));
        }
        // Shared was never subscribed: no production read, even during shutdown.
        try { PollingCoordinator.Shared.RequestRefresh(); Check(false); } catch (ObjectDisposedException) { Check(true); }
        Console.WriteLine($"PASS: {assertions} polling assertions; fake data/manual clock, real STA Dispatcher; no database or SQL.");
    }

    private static Task SnapshotChecks()
    {
        var original = Data();
        Check(original.HasSameData(Data()));
        foreach (var row in new object[] { original.Rooms[0], original.Bookings[0] })
        foreach (var property in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod != null))
        {
            var clone = row.GetType().GetMethod("<Clone>$")!.Invoke(row, null)!;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object value = type == typeof(Nest.Models.RoomOriginalValues)
                ? new Nest.Models.RoomOriginalValues(1, "0101", "Standard", 2, 50, "Verfuegbar", null, null, null, null)
                : type == typeof(string) ? "changed" : type == typeof(int) ? 999 :
                type == typeof(decimal) ? 999m : new DateTime(2030, 1, 1);
            property.SetValue(clone, value);
            var changed = row is RoomSnapshot ? new PollingSnapshot(new[] { (RoomSnapshot)clone }, original.Bookings) :
                new PollingSnapshot(original.Rooms, new[] { (BookingSnapshot)clone });
            Check(!original.HasSameData(changed));
        }
        Check(!original.HasSameData(new PollingSnapshot(Array.Empty<RoomSnapshot>(), original.Bookings)));
        Check(!original.HasSameData(new PollingSnapshot(original.Rooms, Array.Empty<BookingSnapshot>())));
        Check(!original.HasSameData(new PollingSnapshot(original.Rooms.Append(original.Rooms[0] with { Id = 2 }), original.Bookings)));
        Check(!original.HasSameData(new PollingSnapshot(original.Rooms, original.Bookings.Append(original.Bookings[0] with { Code = "B" }))));
        var rooms = original.Rooms.Append(original.Rooms[0] with { Id = 2 }).ToArray();
        Check(new PollingSnapshot(rooms, original.Bookings).HasSameData(new PollingSnapshot(rooms.Reverse(), original.Bookings)));
        try { _ = new PollingSnapshot(rooms.Append(rooms[0]), original.Bookings); Check(false); } catch (InvalidOperationException) { Check(true); }
        return Task.CompletedTask;
    }

    private static async Task IntervalAndChanges(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        Check(coordinator.Interval == TimeSpan.FromSeconds(1));
        try { coordinator.Interval = TimeSpan.Zero; Check(false); } catch (ArgumentOutOfRangeException) { Check(true); }
        var deliveries = 0;
        using var subscription = await ui.Call(() => coordinator.Subscribe(_ => { Check(ui.CheckAccess()); deliveries++; }, _ => Check(ui.CheckAccess())));
        var first = await source.Next(); Check(first.Thread != ui.ThreadId); first.Result.SetResult(Data());
        var tick = await clock.Next(); Check(tick.Duration == TimeSpan.FromSeconds(1)); Check(deliveries == 1);
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(Data());
        tick = await clock.Next(); Check(deliveries == 1); // Equal snapshot does NOT replace a consumer collection.
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(Data("Eingecheckt"));
        _ = await clock.Next(); Check(deliveries == 2);
        coordinator.Interval = TimeSpan.FromSeconds(3);
        (await source.Next()).Result.SetResult(Data("Eingecheckt"));
        Check((await clock.Next()).Duration == TimeSpan.FromSeconds(3));
        Check(source.Maximum == 1);
    }

    private static async Task Errors(Ui ui)
    {
        var source = new Source(); var clock = new Clock(); var statuses = new List<PollingStatus>(); var data = new List<PollingSnapshot>();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        using var subscription = await ui.Call(() => coordinator.Subscribe(data.Add, statuses.Add));
        (await source.Next()).Result.SetResult(Data()); var tick = await clock.Next();
        var retained = data.Single(); var success = statuses.Last().LastSuccess;
        for (var failure = 1; failure <= 8; failure++)
        {
            tick.Done.SetResult(); (await source.Next()).Result.SetException(new IOException("fake technical failure"));
            tick = await clock.Next(); Check(statuses.Last().State == PollingConnectionState.Offline);
            Check(statuses.Last().ConsecutiveFailures == failure); Check(statuses.Last().LastSuccess == success);
            Check(data.Count == 1 && ReferenceEquals(data[0], retained));
            Check(tick.Duration == TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(failure, 6)))));
        }
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(Data());
        tick = await clock.Next(); Check(statuses.Last().State == PollingConnectionState.Online);
        Check(statuses.Last().ConsecutiveFailures == 0 && tick.Duration == TimeSpan.FromSeconds(1)); Check(data.Count == 1);
    }

    private static async Task CoalescingAndStale(Ui ui)
    {
        var source = new Source(); var clock = new Clock(); var data = new List<PollingSnapshot>();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        using var subscription = await ui.Call(() => coordinator.Subscribe(data.Add, _ => { }));
        var first = await source.Next();
        for (var i = 0; i < 100; i++) coordinator.RequestRefresh();
        Check(first.Token.IsCancellationRequested && source.Calls == 1);
        first.Result.SetResult(Data("old"));
        var next = await source.Next(); Check(source.Maximum == 1 && data.Count == 0);
        next.Result.SetResult(Data("new"));
        // A pending signal can cancel the discarded cycle's timer; find the current live timer.
        await clock.Live(); Check(source.Calls == 2 && data.Single().Bookings[0].Status == "new");

        ui.Hold = Signal(); coordinator.RequestRefresh(); (await source.Next()).Result.SetResult(Data("queued-old"));
        await Bounded(ui.Queued.Reader.ReadAsync().AsTask());
        coordinator.RequestRefresh(); ui.Hold.SetResult(); ui.Hold = null;
        (await source.Next()).Result.SetResult(Data("latest"));
        await clock.Live(); Check(data.Count == 2 && data.Last().Bookings[0].Status == "latest");
    }

    private static async Task UnsubscribeAndShutdown(Ui ui)
    {
        var source = new Source(); var clock = new Clock(); var data = 0;
        var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var subscription = await ui.Call(() => coordinator.Subscribe(_ => data++, _ => { }));
        var first = await source.Next(); subscription.Dispose(); Check(first.Token.IsCancellationRequested);
        var stop = coordinator.DisposeAsync().AsTask(); first.Result.SetResult(Data());
        await Bounded(stop); Check(data == 0 && source.Calls == 1);
        await coordinator.DisposeAsync();
        try { coordinator.RequestRefresh(); Check(false); } catch (ObjectDisposedException) { Check(true); }

        source = new Source(); coordinator = new PollingCoordinator(source, ui, clock.Delay);
        subscription = await ui.Call(() => coordinator.Subscribe(_ => data++, _ => { }));
        ui.Hold = Signal(); (await source.Next()).Result.SetResult(Data());
        await Bounded(ui.Queued.Reader.ReadAsync().AsTask()); subscription.Dispose();
        stop = coordinator.DisposeAsync().AsTask(); ui.Hold.SetResult(); ui.Hold = null;
        await Bounded(stop); Check(data == 0);

        source = new Source(); clock = new Clock(); coordinator = new PollingCoordinator(source, ui, clock.Delay);
        subscription = await ui.Call(() => coordinator.Subscribe(_ => data++, _ => { }));
        first = await source.Next(); subscription.Dispose(); first.Result.SetResult(Data("obsolete"));
        var fresh = await ui.Call(() => coordinator.Subscribe(_ => data++, _ => { }));
        (await source.Next()).Result.SetResult(Data("fresh")); await clock.Live();
        Check(data == 1 && source.Maximum == 1); fresh.Dispose(); await coordinator.DisposeAsync();
    }

    private static async Task Consumers(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var data = 0;
        using var bad = await ui.Call(() => coordinator.Subscribe(_ => throw new InvalidOperationException(), _ => { }));
        using var good = await ui.Call(() => coordinator.Subscribe(_ => data++, _ => { }));
        var read = await source.Next();
        // Second registration may invalidate an already-started first read.
        read.Result.SetResult(Data());
        if (read.Token.IsCancellationRequested) (await source.Next()).Result.SetResult(Data());
        await clock.Live(); Check(data == 1 && bad.DeliveryFailed && !good.DeliveryFailed);
        try { coordinator.Subscribe(_ => { }, _ => { }); Check(false); } catch (InvalidOperationException) { Check(true); }
    }

    private static async Task RealAdapter(Ui ui)
    {
        var adapter = new WpfPollingDispatcher();
        Check(!adapter.CheckAccess());
        await adapter.InvokeAsync(() => Check(ui.CheckAccess()), CancellationToken.None);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await adapter.InvokeAsync(() => Check(false), canceled.Token); Check(false); }
        catch (OperationCanceledException) { Check(true); }
    }

    private static async Task AdditionalLifecycle(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        coordinator.RequestRefresh(); Check(source.Calls == 0); // Lazy and no active consumers.
        var count = 0;
        using var first = await ui.Call(() => coordinator.Subscribe(_ => count++, _ => { }));
        (await source.Next()).Result.SetResult(Data()); await clock.Live();
        var secondCount = 0;
        using var second = await ui.Call(() => coordinator.Subscribe(_ => secondCount++, _ => { }));
        (await source.Next()).Result.SetResult(Data()); await clock.Live();
        Check(count == 1 && secondCount == 1); // New consumer receives unchanged current data.
        second.Dispose();
        var canceled = await source.Next();
        coordinator.RequestRefresh();
        canceled.Result.SetCanceled(canceled.Token);
        (await source.Next()).Result.SetResult(Data()); await clock.Live();
        Check(count == 1 && source.Maximum == 1);

        var requested = false;
        using var reentrant = await ui.Call(() => coordinator.Subscribe(_ =>
        { if (!requested) { requested = true; coordinator.RequestRefresh(); } }, _ => { }));
        (await source.Next()).Result.SetResult(Data());
        (await source.Next()).Result.SetResult(Data()); await clock.Live();
        Check(requested && source.Maximum == 1);
    }

    private sealed record Request(CancellationToken Token, int Thread, TaskCompletionSource<PollingSnapshot> Result);
    private sealed class Source : IPollingSource
    {
        private readonly Channel<Request> requests = Channel.CreateUnbounded<Request>();
        private int active;
        internal int Calls, Maximum;
        public async Task<PollingSnapshot> ReadAsync(CancellationToken token)
        {
            var current = Interlocked.Increment(ref active); Maximum = Math.Max(Maximum, current); Interlocked.Increment(ref Calls);
            var result = new TaskCompletionSource<PollingSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Writer.TryWrite(new Request(token, Environment.CurrentManagedThreadId, result));
            try { return await result.Task; } finally { Interlocked.Decrement(ref active); }
        }
        internal Task<Request> Next() => Bounded(requests.Reader.ReadAsync().AsTask());
    }
    private sealed record Tick(TimeSpan Duration, TaskCompletionSource Done);
    private sealed class Clock
    {
        private readonly Channel<Tick> ticks = Channel.CreateUnbounded<Tick>();
        internal Task Delay(TimeSpan duration, CancellationToken token)
        {
            var done = Signal();
            return Wait();
            async Task Wait()
            {
                using var registration = token.Register(() => done.TrySetCanceled(token));
                ticks.Writer.TryWrite(new Tick(duration, done)); await done.Task;
            }
        }
        internal Task<Tick> Next() => Bounded(ticks.Reader.ReadAsync().AsTask());
        internal async Task<Tick> Live()
        { while (true) { var tick = await Next(); if (!tick.Done.Task.IsCompleted) return tick; } }
    }
    private sealed class Ui : IPollingDispatcher, IDisposable
    {
        private readonly Thread thread;
        private readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dispatcher dispatcher;
        internal int ThreadId => thread.ManagedThreadId;
        internal TaskCompletionSource? Hold;
        internal readonly Channel<bool> Queued = Channel.CreateUnbounded<bool>();
        internal Ui()
        {
            thread = new Thread(() => { _ = new Application(); ready.SetResult(Dispatcher.CurrentDispatcher); Dispatcher.Run(); });
            thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
            dispatcher = ready.Task.GetAwaiter().GetResult();
        }
        public bool CheckAccess() => dispatcher.CheckAccess();
        public async Task InvokeAsync(Action action, CancellationToken token)
        {
            var hold = Hold;
            if (hold != null) { Queued.Writer.TryWrite(true); await hold.Task; }
            await dispatcher.InvokeAsync(action, DispatcherPriority.Background, token).Task;
        }
        internal Task<T> Call<T>(Func<T> action) => Bounded(dispatcher.InvokeAsync(action).Task);
        public void Dispose() { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); thread.Join(5000); }
    }
}
