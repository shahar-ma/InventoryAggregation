using System.Collections.Concurrent;
using InventoryAggregation.Api.Inventory;

namespace InventoryAggregation.Tests.Support;

/// <summary>In-memory <see cref="IInventoryPageClient"/>: no HTTP involved. The responder gets (page, requestedPageSize, attemptForThatPage).</summary>
internal sealed class FakePageClient(Func<int, int, int, InventoryPage> responder, TimeSpan? latency = null) : IInventoryPageClient
{
    private readonly ConcurrentQueue<(int Page, int PageSize)> _calls = new();
    private readonly ConcurrentDictionary<int, int> _attempts = new();
    private int _inFlight;
    private int _maxInFlight;

    public IReadOnlyCollection<(int Page, int PageSize)> Calls => _calls;

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public async Task<InventoryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _calls.Enqueue((page, pageSize));
        var attempt = _attempts.AddOrUpdate(page, 1, static (_, n) => n + 1);
        TrackConcurrency(Interlocked.Increment(ref _inFlight));

        try
        {
            if (latency is { } delay)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return responder(page, pageSize, attempt);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private void TrackConcurrency(int current)
    {
        int snapshot;
        do
        {
            snapshot = Volatile.Read(ref _maxInFlight);
            if (current <= snapshot)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maxInFlight, current, snapshot) != snapshot);
    }
}
