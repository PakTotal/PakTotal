using CUE4Parse.Compression;
using Microsoft.Extensions.Logging;

namespace PakTotal.Core;

public static class NativeLibraries
{
    public static async Task InitializeAsync(string nativeDirectory, ILogger logger, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(nativeDirectory, AppContext.BaseDirectory);
        Directory.CreateDirectory(directory);

        await OodleHelper.InitializeAsync(Path.Combine(directory, OodleHelper.OodleFileName), cancellationToken).ConfigureAwait(false);
        await ZlibHelper.InitializeAsync(Path.Combine(directory, ZlibHelper.DllName), cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Native decompression: Oodle {Oodle}, Zlib-ng {Zlib}",
            OodleHelper.Instance is not null, ZlibHelper.Instance is not null);
    }
}
