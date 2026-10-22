using PakTotal.Core.Archives;
using PakTotal.Core.Results;

namespace PakTotal.Core.Scanning;

public enum ScanStatus
{
    Queued,
    Running,
    Done,
    Failed
}

public sealed class ScanJob(ScanRequest request, IDisposable lease, long ticket)
{
    public string Id => Request.ScanId;
    public ScanRequest Request { get; } = request;
    public IDisposable Lease { get; } = lease;
    public long Ticket { get; } = ticket;
    public ScanStatus Status { get; set; } = ScanStatus.Queued;
    public string? InputHash => Inputs?.Hash;
    public ScanInputHashes? Inputs { get; init; }
    public bool Cached { get; init; }
    public ScanOutcome? Outcome { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
}
