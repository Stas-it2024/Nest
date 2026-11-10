using Nest.Data;
using System.Windows;
using System.Windows.Threading;

namespace Nest.Synchronization;

internal sealed class RepositoryPollingSource : IPollingSource
{
    public Task<PollingSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        // Invoked on coordinator's background worker. Synchronous reads cannot be
        // forcibly canceled: wait for their return, never overlap a replacement read.
        cancellationToken.ThrowIfCancellationRequested();
        var bookings = new ReservationRepository().GetAll();
        cancellationToken.ThrowIfCancellationRequested();
        var rooms = new RoomRepository().GetAll();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PollingSnapshot.FromModels(rooms, bookings));
    }
}
internal sealed class WpfPollingDispatcher : IPollingDispatcher
{
    private static Dispatcher Ui => Application.Current?.Dispatcher ??
        throw new InvalidOperationException("WPF application Dispatcher is unavailable.");
    public bool CheckAccess() => Ui.CheckAccess();
    public Task InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        var dispatcher = Ui;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) throw new OperationCanceledException(cancellationToken);
        return dispatcher.InvokeAsync(action, DispatcherPriority.Background, cancellationToken).Task;
    }
}
