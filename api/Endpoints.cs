using Api.Data;
using Api.Practice;

namespace Api;

public sealed record SqlRequest(string Sql);
public sealed record ColumnInfo(string Name, string Type, bool Nullable);
public sealed record TableInfo(string Name, List<ColumnInfo> Columns);

public static class Endpoints
{
    public const string SqlPolicy = "sql";

    public static void MapPracticeApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/exercises", (ExerciseCatalog catalog) =>
            catalog.All.Select(e => new
            {
                e.Id, e.Category, e.Difficulty, e.Title, e.Prompt, e.Hint, e.Ordered,
                Kind = e.Check is null ? "read" : "write",
            }));

        api.MapGet("/{db}/exercises/{id:int}/answer", (string db, int id, ExerciseCatalog catalog) =>
            WithDb(db, d => catalog.Find(id) is { } ex
                ? Results.Ok(new { sql = ex.AnswerFor(d), check = ex.CheckFor(d) })
                : Results.NotFound()));

        api.MapPost("/{db}/run", (string db, SqlRequest req, Runner runner, CancellationToken ct) =>
            WithDbAsync(db, async d => Results.Ok(await runner.RunAsync(d, req.Sql, null, Runner.DisplayRows, ct))))
            .RequireRateLimiting(SqlPolicy);

        api.MapPost("/{db}/exercises/{id:int}/check", (string db, int id, SqlRequest req, ExerciseCatalog catalog, Checker checker, CancellationToken ct) =>
            WithDbAsync(db, async d => catalog.Find(id) is { } ex
                ? Results.Ok(await checker.CheckAsync(d, ex, req.Sql, ct))
                : Results.NotFound()))
            .RequireRateLimiting(SqlPolicy);

        api.MapGet("/{db}/schema", (string db, DbRegistry dbs, CancellationToken ct) =>
            WithDbAsync(db, async d => Results.Ok(await SchemaAsync(dbs, d, ct))));
    }

    static IResult WithDb(string db, Func<Db, IResult> f) =>
        DbRegistry.TryParseDb(db, out var d) ? f(d) : Results.NotFound($"Unknown database '{db}'");

    static async Task<IResult> WithDbAsync(string db, Func<Db, Task<IResult>> f) =>
        DbRegistry.TryParseDb(db, out var d) ? await f(d) : Results.NotFound($"Unknown database '{db}'");

    static async Task<List<TableInfo>> SchemaAsync(DbRegistry dbs, Db db, CancellationToken ct)
    {
        var schema = db == Db.Postgres ? "saleslt" : "SalesLT";
        await using var conn = dbs.Admin(db);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.table_name, c.column_name, c.data_type, c.is_nullable
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = '{schema}' AND t.table_type = 'BASE TABLE'
            ORDER BY c.table_name, c.ordinal_position
            """;
        var tables = new List<TableInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var table = r.GetString(0);
            if (tables.Count == 0 || tables[^1].Name != table) tables.Add(new(table, []));
            tables[^1].Columns.Add(new(r.GetString(1), r.GetString(2), r.GetString(3) == "YES"));
        }
        return tables;
    }
}
