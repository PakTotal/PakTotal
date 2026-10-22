using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakTotal.Core.Results;

namespace PakTotal.Core.Scanning;

public sealed class ScanWorkers(ScanQueue queue, ScanEngine engine, ResultCache cache, IOptions<PakTotalOptions> options,
    ILogger<ScanWorkers> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Value.WorkerCount))
            .Select(_ => Task.Run(() => WorkAsync(stoppingToken), stoppingToken)));

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ScanJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await ProcessAsync(job, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(ScanJob job, CancellationToken stoppingToken)
    {
        job.Status = ScanStatus.Running;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(options.Value.ScanTimeout);
        try
        {
            var outcome = await engine.RunAsync(job.Request, timeout.Token).ConfigureAwait(false);
            job.ScannedAt = DateTimeOffset.UtcNow;
            if (job.InputHash is { } hash && !outcome.HasFailedModules)
                await cache.SaveAsync(hash, outcome, job.ScannedAt.Value, CancellationToken.None).ConfigureAwait(false);
            job.Outcome = outcome;
            job.Status = ScanStatus.Done;
            logger.LogInformation("Scan {ScanId} finished: {Verdict}, incomplete {Incomplete}", job.Id, job.Outcome.Verdict, job.Outcome.Incomplete);
        }
        catch (ScanInputException e)
        {
            job.Error = e.Message;
            job.Status = ScanStatus.Failed;
            logger.LogInformation("Scan {ScanId} rejected: {Reason}", job.Id, e.Message);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            job.Error = "The scan took too long and was stopped.";
            job.Status = ScanStatus.Failed;
            logger.LogWarning("Scan {ScanId} timed out", job.Id);
        }
        catch (Exception e)
        {
            job.Error = "The scan failed.";
            job.Status = ScanStatus.Failed;
            logger.LogError(e, "Scan {ScanId} failed", job.Id);
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            queue.MarkFinished(job);
            TempStorage.TryDelete(job.Request.WorkDirectory, logger);
            job.Lease.Dispose();
        }
    }
}
