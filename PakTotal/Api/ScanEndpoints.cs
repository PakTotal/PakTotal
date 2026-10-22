using Microsoft.Extensions.Options;
using PakTotal.Core;
using PakTotal.Core.Engine;
using PakTotal.Core.Results;
using PakTotal.Core.Scanning;
using PakTotal.Sdk;

namespace PakTotal.Api;

public static class ScanEndpoints
{
    public static IEndpointRouteBuilder MapPakTotalApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").DisableAntiforgery().AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers["X-Api-Version"] = ApiInfo.Version.ToString();
            return await next(context);
        });

        api.MapGet("/info", () => new ApiInfoResponse(ApiInfo.Version.ToString(), SdkInfo.Version.ToString(),
            CoreInfo.Cue4ParseVersion)).RequireRateLimiting(RateLimiting.ReadPolicy);

        api.MapGet("/engine-versions", (EngineVersionCatalog catalog, HttpResponse response) =>
        {
            response.Headers.CacheControl = "public, max-age=3600";
            return new EngineVersionsResponse(catalog.Engines, catalog.Games);
        }).RequireRateLimiting(RateLimiting.ReadPolicy);

        api.MapPost("/scans", SubmitAsync).RequireRateLimiting(RateLimiting.SubmitPolicy);

        api.MapGet("/scans/{id}", (string id, ScanQueue queue) =>
        {
            if (queue.Find(id) is not { } job) return Results.NotFound(new ApiErrorResponse("Unknown scan id."));
            return Results.Ok(ScanStatusResponse.From(job, job.Status == ScanStatus.Queued ? queue.PositionOf(job) : null));
        }).RequireRateLimiting(RateLimiting.ReadPolicy);

        return app;
    }

    private static async Task<IResult> SubmitAsync(HttpRequest request, ScanQueue queue, TempStorage storage, ResultCache cache,
        SiteCounters counters, IOptions<PakTotalOptions> options, ILoggerFactory loggerFactory)
    {
        var limits = options.Value;
        if (request.ContentLength is not { } length) return Results.Json(new ApiErrorResponse("Content-Length is required."), statusCode: 411);
        if (length > limits.MaxUploadBytes) return TooLarge(limits);
        if (queue.IsFull) return Results.Json(new ApiErrorResponse("The scanner is busy. Try again later."), statusCode: 503);

        ByteBudget.Lease lease;
        try
        {
            lease = await queue.Budget.AcquireAsync(length, request.HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }

        var scanId = Guid.NewGuid().ToString("N");
        var directory = storage.CreateScanDirectory(scanId);
        var logger = loggerFactory.CreateLogger("PakTotal.Api");
        IDisposable leases = lease;
        try
        {
            var upload = await ScanUploadReader.ReadAsync(request, scanId, directory, limits.MaxUploadBytes,
                async (bytes, token) => await queue.Budget.AcquireAsync(bytes, token), request.HttpContext.RequestAborted);
            var scanRequest = upload.Request;
            leases = new CompositeLease(lease, upload.ExtraLease);

            var inputs = await ScanInputHash.ComputeAsync(scanRequest, request.HttpContext.RequestAborted);
            if (cache.Enabled)
            {
                var existing = await cache.FindAsync(inputs.Hash, request.HttpContext.RequestAborted) is { } cached
                    ? queue.AddCached(scanRequest, inputs, cached)
                    : queue.FindActive(inputs.Hash);
                if (existing is not null)
                {
                    await counters.IncrementAsync(SiteCounters.Scans);
                    TempStorage.TryDelete(directory, logger);
                    leases.Dispose();
                    logger.LogInformation("Scan {ScanId} matched {Kind} {ExistingId} for input {Hash}", scanId,
                        existing.Cached ? "cached result" : "running scan", existing.Id, inputs.Hash);
                    return Results.Accepted($"/api/v1/scans/{existing.Id}", new ScanSubmittedResponse(existing.Id, Lower(existing.Status)));
                }
            }

            var job = queue.Enqueue(scanRequest, leases, inputs);
            await counters.IncrementAsync(SiteCounters.Scans);
            logger.LogInformation("Scan {ScanId} queued: {Bytes} bytes, {Game}, mappings {Mappings}, encrypted {Encrypted}",
                scanId, scanRequest.TotalBytes, scanRequest.Game, scanRequest.MappingsPath is not null, scanRequest.AesKey is not null);
            return Results.Accepted($"/api/v1/scans/{job.Id}", new ScanSubmittedResponse(job.Id, Lower(job.Status)));
        }
        catch (Exception e)
        {
            TempStorage.TryDelete(directory, logger);
            leases.Dispose();
            return e switch
            {
                ScanInputException input => Results.BadRequest(new ApiErrorResponse(input.Message)),
                UploadTooLargeException or InvalidDataException => TooLarge(limits),
                OperationCanceledException or IOException when request.HttpContext.RequestAborted.IsCancellationRequested => Results.StatusCode(499),
                BadHttpRequestException bad => Results.Json(new ApiErrorResponse("Malformed upload."), statusCode: bad.StatusCode),
                _ => throw e
            };
        }
    }

    private static IResult TooLarge(PakTotalOptions limits) =>
        Results.Json(new ApiErrorResponse($"Uploads are limited to {limits.MaxUploadBytes / (1024 * 1024)} MB."), statusCode: 413);

    private static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();
}
