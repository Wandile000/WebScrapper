using Microsoft.Data.Sqlite;

namespace WebScrapper;

public sealed class ScraperDb : IAsyncDisposable
{
    private readonly SqliteConnection _conn;

    private ScraperDb(SqliteConnection conn) => _conn = conn;

    public static async Task<ScraperDb> OpenAsync(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        var conn = new SqliteConnection(builder.ToString());
        await conn.OpenAsync();

        await using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = """
                CREATE TABLE IF NOT EXISTS scans (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    url         TEXT NOT NULL,
                    host        TEXT NOT NULL,
                    status_code INTEGER NOT NULL,
                    score       INTEGER NOT NULL,
                    reasons     TEXT NOT NULL,
                    elapsed_ms  INTEGER NOT NULL,
                    scanned_at  TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_scans_host ON scans(host);
                CREATE INDEX IF NOT EXISTS ix_scans_score ON scans(score);

                CREATE TABLE IF NOT EXISTS failures (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    url        TEXT NOT NULL,
                    error      TEXT NOT NULL,
                    elapsed_ms INTEGER NOT NULL,
                    occurred_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_failures_url ON failures(url);
                """;
            await ddl.ExecuteNonQueryAsync();
        }

        return new ScraperDb(conn);
    }

    public async Task RecordScanAsync(string url, int score, IEnumerable<string> reasons, int elapsedMs, int statusCode)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scans (url, host, status_code, score, reasons, elapsed_ms, scanned_at)
            VALUES ($url, $host, $status, $score, $reasons, $elapsed, $scanned_at);
            """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$status", statusCode);
        cmd.Parameters.AddWithValue("$score", score);
        cmd.Parameters.AddWithValue("$reasons", string.Join("; ", reasons));
        cmd.Parameters.AddWithValue("$elapsed", elapsedMs);
        cmd.Parameters.AddWithValue("$scanned_at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordFailureAsync(string url, string error, int elapsedMs)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO failures (url, error, elapsed_ms, occurred_at)
            VALUES ($url, $error, $elapsed, $at);
            """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$error", error);
        cmd.Parameters.AddWithValue("$elapsed", elapsedMs);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.DisposeAsync();
    }
}
