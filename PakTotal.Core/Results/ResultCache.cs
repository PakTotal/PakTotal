using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakTotal.Core.Modules;
using PakTotal.Core.Scanning;

namespace PakTotal.Core.Results;

public sealed record CachedResult(ScanOutcome Outcome, DateTimeOffset ScannedAt);

public sealed class ResultCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string? _connectionString;
    private readonly long _maxBytes;
    private readonly SemaphoreSlim _pruneLock = new(1, 1);
    private readonly ILogger<ResultCache> _logger;

    public ResultCache(ModuleCatalog catalog, IOptions<PakTotalOptions> options, ILogger<ResultCache> logger)
    {
        _logger = logger;
        if (string.IsNullOrWhiteSpace(options.Value.DatabasePath))
        {
            logger.LogInformation("Result cache is disabled");
            Fingerprint = "";
            return;
        }

        var path = Path.GetFullPath(options.Value.DatabasePath, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        Fingerprint = ScannerFingerprint.Compute(catalog);
        _maxBytes = options.Value.MaxDatabaseBytes;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              PRAGMA journal_mode = WAL;
                              CREATE TABLE IF NOT EXISTS results (
                                  hash TEXT NOT NULL PRIMARY KEY,
                                  fingerprint TEXT NOT NULL,
                                  scanned_at TEXT NOT NULL,
                                  result TEXT NOT NULL
                              ) WITHOUT ROWID;
                              CREATE INDEX IF NOT EXISTS results_scanned_at ON results (scanned_at);
                              """;
        command.ExecuteNonQuery();
        Prune(connection);
        logger.LogInformation("Result cache at {Path}, scanner fingerprint {Fingerprint}", path, Fingerprint[..12]);
    }

    public bool Enabled => _connectionString is not null;

    public string Fingerprint { get; }

    public async Task<CachedResult?> FindAsync(string hash, CancellationToken cancellationToken)
    {
        if (!Enabled) return null;
        try
        {
            await using var connection = Open();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT scanned_at, result FROM results WHERE hash = $hash AND fingerprint = $fingerprint";
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$fingerprint", Fingerprint);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

            var scannedAt = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var outcome = JsonSerializer.Deserialize<ScanOutcome>(reader.GetString(1), JsonOptions);
            return outcome is null ? null : new CachedResult(outcome, scannedAt);
        }
        catch (Exception e) when (e is SqliteException or JsonException or FormatException)
        {
            _logger.LogWarning(e, "Could not read cached result {Hash}", hash);
            return null;
        }
    }

    public async Task SaveAsync(string hash, ScanOutcome outcome, DateTimeOffset scannedAt, CancellationToken cancellationToken)
    {
        if (!Enabled) return;
        try
        {
            await using var connection = Open();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO results (hash, fingerprint, scanned_at, result) VALUES ($hash, $fingerprint, $scannedAt, $result)
                                  ON CONFLICT (hash) DO UPDATE SET fingerprint = excluded.fingerprint, scanned_at = excluded.scanned_at,
                                      result = excluded.result
                                  """;
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$fingerprint", Fingerprint);
            command.Parameters.AddWithValue("$scannedAt", scannedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(outcome, JsonOptions));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await PruneAsync(connection).ConfigureAwait(false);
        }
        catch (SqliteException e)
        {
            _logger.LogWarning(e, "Could not cache result {Hash}", hash);
        }
    }

    private async Task PruneAsync(SqliteConnection connection)
    {
        if (_maxBytes <= 0 || !await _pruneLock.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            Prune(connection);
        }
        finally
        {
            _pruneLock.Release();
        }
    }

    private void Prune(SqliteConnection connection)
    {
        if (_maxBytes <= 0) return;
        for (var pass = 0; pass < 10; pass++)
        {
            var used = Scalar(connection, "SELECT (page_count - freelist_count) * page_size FROM pragma_page_count, pragma_freelist_count, pragma_page_size");
            if (used <= _maxBytes) return;
            var rows = Scalar(connection, "SELECT COUNT(*) FROM results");
            if (rows == 0) return;

            var excess = (double)(used - _maxBytes) / used;
            var remove = Math.Clamp((long)Math.Ceiling(rows * (excess + 0.01)), 1, rows);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM results WHERE hash IN (SELECT hash FROM results ORDER BY scanned_at LIMIT $count)";
            command.Parameters.AddWithValue("$count", remove);
            command.ExecuteNonQuery();
            _logger.LogInformation("Result cache over {Limit} bytes ({Used} used), removed {Count} oldest results", _maxBytes, used, remove);
        }
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
