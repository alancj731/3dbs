using System.Data.Common;
using System.Diagnostics;
using Api.Data;

namespace Api.Practice;

public sealed record QueryResult(
    string[] Columns,
    List<object?[]> Rows,
    int RowsAffected,
    bool Truncated,
    long ElapsedMs);

/// <summary>The result of one run: the user's statement, plus the check query run after it in the same transaction.</summary>
public sealed record RunOutcome(QueryResult? Result, QueryResult? Check, string? Error);

/// <summary>
/// Runs SQL as the restricted "practice" login, always inside a transaction that is rolled back,
/// so INSERT/UPDATE/DELETE practice never changes the data.
/// </summary>
public sealed class Runner(DbRegistry dbs)
{
    public const int DisplayRows = 500;
    public const int CompareRows = 5000;
    const int TimeoutSeconds = 5;
    const int MaxSqlLength = 20_000;

    public async Task<RunOutcome> RunAsync(Db db, string sql, string? checkSql, int maxRows, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql)) return new(null, null, "Enter a SQL statement first.");
        if (sql.Length > MaxSqlLength) return new(null, null, $"SQL is too long (max {MaxSqlLength:N0} characters).");
        if (SqlGuard.Validate(db, sql) is { } rejected) return new(null, null, rejected);

        await using var conn = dbs.Practice(db);
        try
        {
            await conn.OpenAsync(ct);
        }
        catch (Exception ex)
        {
            return new(null, null, $"Could not connect to {db}: {ex.Message}");
        }

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var result = await ExecuteAsync(conn, tx, sql, maxRows, ct);
            var check = checkSql is null ? null : await ExecuteAsync(conn, tx, checkSql, CompareRows, ct);
            return new(result, check, null);
        }
        catch (DbException ex)
        {
            return new(null, null, ex.Message);
        }
        finally
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch (DbException) { /* aborted tx: nothing to undo */ }
        }
    }

    /// <summary>Runs a batch. Returns the last result set that has columns (or just the row count for DML).</summary>
    static async Task<QueryResult> ExecuteAsync(DbConnection conn, DbTransaction tx, string sql, int maxRows, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = TimeoutSeconds;

        var sw = Stopwatch.StartNew();
        string[] columns = [];
        var rows = new List<object?[]>();
        var truncated = false;
        int affected;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            do
            {
                if (reader.FieldCount == 0) continue;
                columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                rows = [];
                truncated = false;
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count == maxRows) { truncated = true; break; }
                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++) row[i] = ToJsonValue(reader.GetValue(i));
                    rows.Add(row);
                }
            } while (await reader.NextResultAsync(ct));
            await reader.CloseAsync();
            affected = reader.RecordsAffected;
        }
        return new(columns, rows, Math.Max(affected, 0), truncated, sw.ElapsedMilliseconds);
    }

    static object? ToJsonValue(object v) => v switch
    {
        DBNull => null,
        byte[] b => $"0x{Convert.ToHexString(b.AsSpan(0, Math.Min(b.Length, 16)))}{(b.Length > 16 ? "…" : "")} ({b.Length} bytes)",
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm:ss.FFF"),
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss.FFF zzz"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeSpan or TimeOnly => v.ToString(),
        Guid g => g.ToString(),
        float f => (double)f,
        string or bool or decimal or double or long or int or short or byte or sbyte or ushort or uint or ulong => v,
        _ => v.ToString(),
    };
}
