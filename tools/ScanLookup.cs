#!/usr/bin/env dotnet
#:package Microsoft.Data.Sqlite@10.0.13

using System.Text.Json;
using Microsoft.Data.Sqlite;

string? hash = null;
var dbPath = Path.Combine("artifacts", "paktotal.db");
var rawJson = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db" when i + 1 < args.Length: dbPath = args[++i]; break;
        case "--json": rawJson = true; break;
        default: hash = args[i].Trim().ToLowerInvariant(); break;
    }
}

if (hash is null || hash.Length < 8 || !hash.All(Uri.IsHexDigit))
{
    Console.Error.WriteLine("Usage: dotnet run tools/ScanLookup.cs -- <hash> [--db <path>] [--json]");
    Console.Error.WriteLine("The hash must be at least 8 hex characters.");
    return 2;
}
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {Path.GetFullPath(dbPath)}");
    return 2;
}

var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();
await using var command = connection.CreateCommand();
command.CommandText = """
                      SELECT hash, fingerprint, scanned_at, result FROM results
                      WHERE hash >= $prefix AND hash < $prefix || 'g' ORDER BY scanned_at DESC LIMIT 20
                      """;
command.Parameters.AddWithValue("$prefix", hash);

var rows = new List<(string Hash, string Fingerprint, string ScannedAt, string Result)>();
await using (var reader = await command.ExecuteReaderAsync())
{
    while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
}

if (rows.Count == 0)
{
    Console.WriteLine($"No stored result for {hash}.");
    Console.WriteLine("It may never have been stored (a module failed, or the cache was disabled) or was pruned. Ask for the files.");
    return 1;
}
if (rows.Count > 1)
{
    Console.WriteLine($"{rows.Count} results match {hash}, use a longer prefix:");
    foreach (var row in rows) Console.WriteLine($"  {row.Hash}  {row.ScannedAt}");
    return 1;
}

var (fullHash, fingerprint, scannedAt, result) = rows[0];
if (rawJson)
{
    Console.WriteLine(result);
    return 0;
}

using var document = JsonDocument.Parse(result);
var outcome = document.RootElement;
Console.WriteLine($"Scan hash:   {fullHash}");
Console.WriteLine($"Scanned at:  {scannedAt}");
Console.WriteLine($"Fingerprint: {fingerprint}");
Console.WriteLine($"Verdict:     {Text(outcome, "verdict")} ({Text(outcome, "severity") ?? "no severity"})" +
                  (Flag(outcome, "incomplete") ? ", incomplete" : ""));

foreach (var module in Items(outcome, "modules"))
{
    Console.WriteLine();
    Console.WriteLine($"{Text(module, "name")}{(Flag(module, "isPrivate") ? " (private)" : "")}: {Text(module, "verdict")}" +
                      (Text(module, "severity") is { } severity ? $" {severity}" : "") + (Flag(module, "incomplete") ? ", incomplete" : ""));
    foreach (var asset in Items(module, "assets"))
    {
        Console.WriteLine($"  {Text(asset, "path")}: {Text(asset, "verdict")}" + (Text(asset, "severity") is { } assetSeverity ? $" {assetSeverity}" : ""));
        foreach (var reason in Items(asset, "reasons")) Console.WriteLine($"    - [{Text(reason, "severity")}] {Text(reason, "text")}");
    }
}
return 0;

static string? Text(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;

static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
