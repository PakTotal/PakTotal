using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PakTotal.Core.Archives;
using PakTotal.Core.Engine;
using PakTotal.Core.Modules;
using PakTotal.Core.Results;
using PakTotal.Core.Scanning;

namespace PakTotal.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPakTotalCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PakTotalOptions>(configuration.GetSection(PakTotalOptions.Section));
        services.AddSingleton<EngineVersionCatalog>();
        services.AddSingleton<ModuleCatalog>();
        services.AddSingleton<MappingsCache>();
        services.AddSingleton<ArchiveLoader>();
        services.AddSingleton<ScanEngine>();
        services.AddSingleton<ResultCache>();
        services.AddSingleton<SiteCounters>();
        services.AddSingleton<ScanQueue>();
        services.AddSingleton<TempStorage>();
        services.AddHostedService(sp => sp.GetRequiredService<TempStorage>());
        services.AddHostedService<ScanWorkers>();
        return services;
    }
}
