namespace PakTotal.Core.Scanning;

public sealed class ByteBudget(long capacity)
{
    private readonly object _lock = new();
    private readonly LinkedList<(long Bytes, TaskCompletionSource<Lease> Waiter)> _waiters = [];
    private long _used;

    public long Capacity => capacity;

    public long Used
    {
        get
        {
            lock (_lock) return _used;
        }
    }

    public int Waiting
    {
        get
        {
            lock (_lock) return _waiters.Count;
        }
    }

    public Task<Lease> AcquireAsync(long bytes, CancellationToken cancellationToken)
    {
        if (bytes > capacity) throw new ArgumentOutOfRangeException(nameof(bytes), "Request exceeds total byte budget");
        lock (_lock)
        {
            if (_waiters.Count == 0 && _used + bytes <= capacity)
            {
                _used += bytes;
                return Task.FromResult(new Lease(this, bytes));
            }

            var waiter = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
            var node = _waiters.AddLast((bytes, waiter));
            cancellationToken.Register(() =>
            {
                lock (_lock)
                {
                    if (node.List is null) return;
                    _waiters.Remove(node);
                }
                waiter.TrySetCanceled(cancellationToken);
            });
            return waiter.Task;
        }
    }

    private void Release(long bytes)
    {
        var ready = new List<(TaskCompletionSource<Lease> Waiter, Lease Lease)>();
        lock (_lock)
        {
            _used -= bytes;
            while (_waiters.First is { } first && _used + first.Value.Bytes <= capacity)
            {
                _waiters.RemoveFirst();
                _used += first.Value.Bytes;
                ready.Add((first.Value.Waiter, new Lease(this, first.Value.Bytes)));
            }
        }
        foreach (var (waiter, lease) in ready)
        {
            if (!waiter.TrySetResult(lease)) lease.Dispose();
        }
    }

    public sealed class Lease(ByteBudget owner, long bytes) : IDisposable
    {
        private int _disposed;

        public long Bytes => bytes;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(bytes);
        }
    }
}
