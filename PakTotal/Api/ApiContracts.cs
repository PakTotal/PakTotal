using PakTotal.Core.Engine;
using PakTotal.Core.Scanning;
using PakTotal.Sdk;
using PakTotal.Sdk.Modules;

namespace PakTotal.Api;

public static class ApiInfo
{
    public static readonly SemVer Version = new(1, 0, 0);
}

public sealed record ApiInfoResponse(string Api, string Sdk, string Cue4Parse);

public sealed record EngineVersionsResponse(IReadOnlyList<EngineVersionEntry> Engines, IReadOnlyList<EngineVersionEntry> Games);

public sealed record ScanSubmittedResponse(string Id, string Status);

public sealed record ReasonResponse(string Text, string Severity);

public sealed record AssetResultResponse(string Path, string Verdict, string? Severity, IReadOnlyList<ReasonResponse> Reasons);

public sealed record ModuleResultResponse(string Name, bool IsPrivate, string Verdict, string? Severity, bool Incomplete,
    IReadOnlyList<AssetResultResponse> Assets);

public sealed record FileHashResponse(string Name, string Role, long Size, string Sha256);

public sealed record ScanStatusResponse(string Id, string Status, int? Position, string? Verdict, string? Severity, bool? Incomplete, string? Error,
    IReadOnlyList<ModuleResultResponse>? Modules, string? Hash = null, IReadOnlyList<FileHashResponse>? Files = null, bool Cached = false,
    DateTimeOffset? ScannedAt = null)
{
    public static ScanStatusResponse From(ScanJob job, int? position)
    {
        var outcome = job.Status == ScanStatus.Done ? job.Outcome : null;
        return new ScanStatusResponse(job.Id, Lower(job.Status), position, outcome is null ? null : Lower(outcome.Verdict),
            LowerOrNull(outcome?.Severity), outcome?.Incomplete, job.Error, outcome?.Modules.Select(ToResponse).ToList(), job.InputHash,
            job.Inputs?.Files.Select(f => new FileHashResponse(f.Name, f.Role, f.Size, f.Sha256)).ToList(), job.Cached,
            outcome is null ? null : job.ScannedAt);
    }

    private static ModuleResultResponse ToResponse(ModuleReport report) =>
        new(report.Name, report.IsPrivate, Lower(report.Verdict), LowerOrNull(report.Severity), report.Incomplete,
            report.Assets.Select(a => new AssetResultResponse(a.Path, Lower(a.Verdict), LowerOrNull(a.Severity),
                a.Reasons.Select(r => new ReasonResponse(r.Text, Lower(r.Severity))).ToList())).ToList());

    private static string? LowerOrNull(Severity? severity) => severity is { } value ? Lower(value) : null;

    public static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();
}

public sealed record ApiErrorResponse(string Error);

public sealed class CompositeLease(params IDisposable?[] leases) : IDisposable
{
    public void Dispose()
    {
        foreach (var lease in leases) lease?.Dispose();
    }
}
