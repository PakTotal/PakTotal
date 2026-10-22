namespace PakTotal.Core;

public sealed class PakTotalOptions
{
    public const string Section = "PakTotal";

    public string TempDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "paktotal");
    public string ModulesDirectory { get; set; } = "modules";
    public string NativeDirectory { get; set; } = "native";
    public string DatabasePath { get; set; } = "";
    public long MaxDatabaseBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public int MappingsCacheEntries { get; set; } = 2;
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public long MaxConcurrentBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public int MaxQueueLength { get; set; } = 200;
    public int WorkerCount { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan ModuleTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan ResultRetention { get; set; } = TimeSpan.FromMinutes(15);
    public int SubmitPermitsPerMinute { get; set; } = 10;
    public int ReadPermitsPerMinute { get; set; } = 120;
    public List<string> TrustedProxies { get; set; } = [];
    public List<string> TrustedNetworks { get; set; } = [];
}
