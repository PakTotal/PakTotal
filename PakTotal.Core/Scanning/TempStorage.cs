using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PakTotal.Core.Scanning;

public sealed class TempStorage(IOptions<PakTotalOptions> options, ScanQueue queue, ILogger<TempStorage> logger) : BackgroundService
{
    public string Root { get; } = Path.GetFullPath(options.Value.TempDirectory, AppContext.BaseDirectory);

    public string CreateScanDirectory(string scanId)
    {
        var path = Path.Combine(Root, scanId);
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDelete(string path, ILogger logger)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2) logger.LogWarning(e, "Could not delete scan directory {Path}", path);
                Thread.Sleep(200);
            }
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(Root))
        {
            foreach (var directory in Directory.GetDirectories(Root)) TryDelete(directory, logger);
        }
        Directory.CreateDirectory(Root);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) queue.RemoveExpired(DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
