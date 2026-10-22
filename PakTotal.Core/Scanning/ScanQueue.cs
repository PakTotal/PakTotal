using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PakTotal.Core.Archives;
using PakTotal.Core.Results;

namespace PakTotal.Core.Scanning;

public sealed class ScanQueue
{
    private readonly Channel<ScanJob> _channel;
    private readonly ConcurrentDictionary<string, ScanJob> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ScanJob> _active = new(StringComparer.Ordinal);
    private readonly PakTotalOptions _options;
    private long _nextTicket;
    private long _startedTicket;
    private int _pending;

    public ScanQueue(IOptions<PakTotalOptions> options)
    {
        _options = options.Value;
        Budget = new ByteBudget(_options.MaxConcurrentBytes);
        _channel = Channel.CreateUnbounded<ScanJob>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    }

    public ByteBudget Budget { get; }

    public int Pending => Volatile.Read(ref _pending);

    public int Running => _jobs.Values.Count(j => j.Status == ScanStatus.Running);

    public bool IsFull => Volatile.Read(ref _pending) + Budget.Waiting >= _options.MaxQueueLength;

    public ScanJob Enqueue(ScanRequest request, IDisposable lease, ScanInputHashes? inputs = null)
    {
        var job = new ScanJob(request, lease, Interlocked.Increment(ref _nextTicket)) { Inputs = inputs };
        _jobs[job.Id] = job;
        if (inputs is not null) _active[inputs.Hash] = job;
        Interlocked.Increment(ref _pending);
        if (!_channel.Writer.TryWrite(job)) throw new InvalidOperationException("Scan queue is closed");
        return job;
    }

    public async ValueTask<ScanJob> DequeueAsync(CancellationToken cancellationToken)
    {
        var job = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Decrement(ref _pending);
        InterlockedMax(ref _startedTicket, job.Ticket);
        return job;
    }

    public ScanJob AddCached(ScanRequest request, ScanInputHashes inputs, CachedResult cached)
    {
        var job = new ScanJob(request, NoLease.Instance, 0)
        {
            Inputs = inputs,
            Cached = true,
            Status = ScanStatus.Done,
            Outcome = cached.Outcome,
            ScannedAt = cached.ScannedAt,
            FinishedAt = DateTimeOffset.UtcNow
        };
        _jobs[job.Id] = job;
        return job;
    }

    public ScanJob? FindActive(string inputHash) =>
        _active.TryGetValue(inputHash, out var job) && job.Status is ScanStatus.Queued or ScanStatus.Running ? job : null;

    public void MarkFinished(ScanJob job)
    {
        if (job.InputHash is { } hash) _active.TryRemove(new KeyValuePair<string, ScanJob>(hash, job));
    }

    public ScanJob? Find(string id) => _jobs.GetValueOrDefault(id);

    public int PositionOf(ScanJob job) => job.Status == ScanStatus.Queued ? (int)Math.Max(1, job.Ticket - Volatile.Read(ref _startedTicket)) : 0;

    public void RemoveExpired(DateTimeOffset now)
    {
        foreach (var (id, job) in _jobs)
        {
            if (job.FinishedAt is { } finished && now - finished > _options.ResultRetention) _jobs.TryRemove(id, out _);
        }
    }

    private sealed class NoLease : IDisposable
    {
        public static readonly NoLease Instance = new();

        public void Dispose()
        {
        }
    }

    private static void InterlockedMax(ref long target, long value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current) return;
            current = previous;
        }
    }
}
