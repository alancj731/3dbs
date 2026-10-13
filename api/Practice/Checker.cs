using System.Diagnostics;
using System.Globalization;
using Api.Data;

namespace Api.Practice;

public sealed record CheckResult(
    bool Correct,
    string Message,
    QueryResult? Yours,
    QueryResult? Expected,
    List<object?[]> Missing,
    List<object?[]> Extra,
    string? Error);

/// <summary>
/// Grades a submission by running it and the reference answer on the same database and comparing
/// the results. Column names are ignored (aliases are free), values are normalized
/// (2-decimal rounding, bool = 1/0, trailing CHAR padding), and row order only matters
/// when the exercise says so.
/// </summary>
public sealed class Checker(Runner runner, ILogger<Checker> log)
{
    const int MaxDiffRows = 20;
    const int SlowCheckMs = 2000;

    public async Task<CheckResult> CheckAsync(Db db, Exercise ex, string sql, CancellationToken ct)
    {
        var checkSql = ex.CheckFor(db);
        var sw = Stopwatch.StartNew();
        var mine = await runner.RunAsync(db, sql, checkSql, Runner.CompareRows, ct);
        var mineMs = sw.ElapsedMilliseconds;
        if (mine.Error is not null)
            return new(false, "Your SQL failed to run.", null, null, [], [], mine.Error);

        var reference = await runner.RunAsync(db, ex.AnswerFor(db), checkSql, Runner.CompareRows, ct);
        if (sw.ElapsedMilliseconds >= SlowCheckMs)
            log.LogWarning("Slow check: {Db} #{Id} took {Ms} ms (yours {MineMs} ms, reference {RefMs} ms)",
                db, ex.Id, sw.ElapsedMilliseconds, mineMs, sw.ElapsedMilliseconds - mineMs);
        if (reference.Error is not null)
            return new(false, "The reference answer failed to run (this is a bug in the exercise).", Trim(mine.Result), null, [], [], reference.Error);

        // Write exercises compare the table state after the statement, read exercises the result itself.
        var yours = checkSql is null ? mine.Result! : mine.Check!;
        var expected = checkSql is null ? reference.Result! : reference.Check!;
        var (correct, message, missing, extra) = Compare(yours, expected, ex.Ordered, isWrite: checkSql is not null);
        return new(correct, message, Trim(yours), Trim(expected), missing, extra, null);
    }

    internal static (bool Correct, string Message, List<object?[]> Missing, List<object?[]> Extra) Compare(QueryResult yours, QueryResult expected, bool ordered, bool isWrite)
    {
        var what = isWrite ? "After your statement the data" : "Your result";
        if (yours.Columns.Length != expected.Columns.Length)
            return (false, $"{what} has {yours.Columns.Length} column(s); expected {expected.Columns.Length}.", [], []);

        var mine = yours.Rows.Select(Key).ToList();
        var theirs = expected.Rows.Select(Key).ToList();

        if (ordered && mine.SequenceEqual(theirs))
            return (true, "Correct!", [], []);

        // Multiset difference in both directions.
        var counts = theirs.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var extra = new List<object?[]>();
        for (var i = 0; i < mine.Count; i++)
        {
            if (counts.TryGetValue(mine[i], out var n) && n > 0) counts[mine[i]] = n - 1;
            else if (extra.Count < MaxDiffRows) extra.Add(yours.Rows[i]);
        }
        // Whatever is left in counts is missing; take that many copies of each row, not every copy.
        var missing = new List<object?[]>();
        for (var i = 0; i < theirs.Count && missing.Count < MaxDiffRows; i++)
        {
            if (counts[theirs[i]] == 0) continue;
            counts[theirs[i]]--;
            missing.Add(expected.Rows[i]);
        }
        var sameRows = missing.Count == 0 && extra.Count == 0 && mine.Count == theirs.Count;

        if (sameRows && !ordered) return (true, "Correct!", [], []);
        if (sameRows) return (false, "The rows are right but the order is wrong. Check your ORDER BY.", [], []);

        var msg = mine.Count != theirs.Count
            ? $"{what} has {mine.Count} row(s); expected {theirs.Count}."
            : $"{what} has the right number of rows ({mine.Count}), but some values differ.";
        return (false, msg, missing, extra);
    }

    static string Key(object?[] row) => string.Join("\u001f", row.Select(Normalize));

    static string Normalize(object? v) => v switch
    {
        null => "∅",
        bool b => b ? "1" : "0",
        decimal d => Math.Round(d, 2).ToString("0.##", CultureInfo.InvariantCulture),
        double d => Math.Round((decimal)d, 2).ToString("0.##", CultureInfo.InvariantCulture),
        long or int or short or byte or sbyte or ushort or uint or ulong =>
            Convert.ToDecimal(v, CultureInfo.InvariantCulture).ToString("0", CultureInfo.InvariantCulture),
        string s => s.TrimEnd(),
        _ => v.ToString() ?? "",
    };

    static QueryResult? Trim(QueryResult? r) =>
        r is null || r.Rows.Count <= Runner.DisplayRows ? r : r with { Rows = r.Rows.Take(Runner.DisplayRows).ToList(), Truncated = true };
}
