using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakTotal.Core;
using PakTotal.Core.Archives;
using PakTotal.Core.Engine;
using PakTotal.Core.Modules;
using PakTotal.Core.Scanning;
using PakTotal.Sdk.Modules;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
var filter = args.Length > 1 ? args[1] : null;
using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

var options = Options.Create(new PakTotalOptions
{
    ModulesDirectory = Path.Combine(root, "artifacts", "modules"),
    NativeDirectory = Path.Combine(root, "native"),
    TempDirectory = Path.Combine(root, "artifacts", "temp")
});
await NativeLibraries.InitializeAsync(options.Value.NativeDirectory, loggerFactory.CreateLogger("Natives"));
var catalog = new ModuleCatalog(options, loggerFactory.CreateLogger<ModuleCatalog>());
var loader = new ArchiveLoader(new MappingsCache(options, loggerFactory.CreateLogger<MappingsCache>()), loggerFactory);

var failures = 0;
var cases = new[] { "Modules", "Modules.Private" }
    .Select(d => Path.Combine(root, d))
    .Where(Directory.Exists)
    .SelectMany(d => Directory.EnumerateFiles(d, "case.json", SearchOption.AllDirectories))
    .Select(f => Path.GetDirectoryName(f)!)
    .Where(c => Path.GetFileName(Path.GetDirectoryName(c)) == "cases" && File.Exists(Path.Combine(c, "..", "..", "..", "module.json")))
    .Order(StringComparer.OrdinalIgnoreCase)
    .Where(c => filter is null || c.Contains(filter, StringComparison.OrdinalIgnoreCase))
    .ToList();

foreach (var caseDirectory in cases)
{
    var moduleDirectory = Path.GetFullPath(Path.Combine(caseDirectory, "..", "..", ".."));
    var manifestJson = File.ReadAllText(Path.Combine(moduleDirectory, "module.json"));
    var manifest = JsonSerializer.Deserialize<ModuleManifest>(manifestJson, ModuleManifest.JsonOptions)!;
    var name = $"{manifest.Id}/{Path.GetFileName(caseDirectory)}";
    var mismatches = await RunCaseAsync(caseDirectory, manifest);

    if (mismatches.Count == 0)
    {
        Console.WriteLine($"PASS {name}");
        continue;
    }
    failures++;
    Console.WriteLine($"FAIL {name}");
    foreach (var mismatch in mismatches) Console.WriteLine($"    {mismatch}");
}

Console.WriteLine(cases.Count == 0 ? "No test cases found" : $"{cases.Count - failures}/{cases.Count} cases passed");
return failures == 0 ? 0 : 1;

async Task<List<string>> RunCaseAsync(string caseDirectory, ModuleManifest manifest)
{
    var module = catalog.Modules.FirstOrDefault(m => m.Id == manifest.Id);
    if (module is null) return ["module is not built or not loaded"];

    var testCase = JsonSerializer.Deserialize<TestCase>(File.ReadAllText(Path.Combine(caseDirectory, "case.json")), ModuleManifest.JsonOptions)!;
    if (!EngineVersionCatalog.TryParseGame(testCase.EngineVersion, out var game)) return [$"unknown engine version {testCase.EngineVersion}"];

    var request = new ScanRequest
    {
        ScanId = "test",
        WorkDirectory = caseDirectory,
        Game = game,
        ArchivePaths = testCase.Archives.Select(a => Path.GetFullPath(Path.Combine(caseDirectory, a))).ToList(),
        GlobalPaths = testCase.Globals.Select(a => Path.GetFullPath(Path.Combine(caseDirectory, a))).ToList(),
        MappingsPath = testCase.Mappings is null ? null : Path.GetFullPath(Path.Combine(caseDirectory, testCase.Mappings)),
        AesKey = testCase.AesKey
    };

    ModuleReport report;
    try
    {
        using var scan = await loader.LoadAsync(request, CancellationToken.None);
        var context = scan.Context;
        var outcome = catalog.IsApplicable(module, context)
            ? new ModuleOutcome(module, await module.Instance.AnalyseAsync(context, CancellationToken.None))
            : new ModuleOutcome(module, new ModuleResult(), ModuleRunState.Skipped);
        report = VerdictAggregator.Aggregate([outcome], context.LoadProblems.Count).Modules.Single();
    }
    catch (ScanInputException e)
    {
        report = new ModuleReport(manifest.Name, false, Verdict.Unprocessable, null, false, []);
        Console.WriteLine($"    note: scan rejected: {e.Message}");
    }

    var mismatches = new List<string>();
    var actualModule = report.Verdict.ToString().ToLowerInvariant();
    if (testCase.ModuleVerdict is { } expectedModule && !expectedModule.Equals(actualModule, StringComparison.OrdinalIgnoreCase))
        mismatches.Add($"module verdict: expected {expectedModule}, got {actualModule}");

    var actual = report.Assets.ToDictionary(a => a.Path, Describe, StringComparer.OrdinalIgnoreCase);
    foreach (var (asset, expected) in testCase.Expected)
    {
        var got = actual.GetValueOrDefault(asset, "undetected");
        if (!Matches(expected, got)) mismatches.Add($"{asset}: expected {expected}, got {got}");
    }
    foreach (var (asset, got) in actual.Where(a => !testCase.Expected.ContainsKey(a.Key) && a.Value != "undetected"))
        mismatches.Add($"{asset}: not in expected list, got {got}");
    return mismatches;
}

static string Describe(AssetReport asset)
{
    var verdict = asset.Verdict.ToString().ToLowerInvariant();
    return asset.Severity is { } severity ? $"{verdict}:{severity.ToString().ToLowerInvariant()}" : verdict;
}

static bool Matches(string expected, string actual) =>
    expected.Contains(':') ? expected.Equals(actual, StringComparison.OrdinalIgnoreCase)
        : expected.Equals(actual.Split(':')[0], StringComparison.OrdinalIgnoreCase);

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PakTotal.sln"))) directory = directory.Parent;
    return directory?.FullName ?? throw new InvalidOperationException("Could not find PakTotal.sln");
}

internal sealed class TestCase
{
    public string EngineVersion { get; init; } = "";
    public List<string> Archives { get; init; } = [];
    public List<string> Globals { get; init; } = [];
    public string? Mappings { get; init; }
    public string? AesKey { get; init; }
    public string? ModuleVerdict { get; init; }
    public Dictionary<string, string> Expected { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
