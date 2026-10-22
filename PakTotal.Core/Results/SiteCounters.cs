using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PakTotal.Core.Results;

public sealed class SiteCounters
{
    public const string Visits = "visits";
    public const string Scans = "scans";

    private readonly string? _connectionString;
    private readonly ILogger<SiteCounters> _logger;

    public SiteCounters(IOptions<PakTotalOptions> options, ILogger<SiteCounters> logger)
    {
        _logger = logger;
        if (string.IsNullOrWhiteSpace(options.Value.DatabasePath)) return;

        var path = Path.GetFullPath(options.Value.DatabasePath, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS counters (name TEXT NOT NULL PRIMARY KEY, value INTEGER NOT NULL) WITHOUT ROWID";
        command.ExecuteNonQuery();
    }

    public bool Enabled => _connectionString is not null;

    public Task<long?> IncrementAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(name, "INSERT INTO counters (name, value) VALUES ($name, 1) ON CONFLICT (name) DO UPDATE SET value = value + 1 RETURNING value",
            cancellationToken);

    public Task<long?> GetAsync(string name, CancellationToken cancellationToken = default) =>
        RunAsync(name, "SELECT value FROM counters WHERE name = $name", cancellationToken);

    private async Task<long?> RunAsync(string name, string sql, CancellationToken cancellationToken)
    {
        if (!Enabled) return null;
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$name", name);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is long value ? value : 0;
        }
        catch (SqliteException e)
        {
            _logger.LogWarning(e, "Could not update counter {Counter}", name);
            return null;
        }
    }
}
