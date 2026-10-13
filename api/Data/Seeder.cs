using System.Data.Common;
using System.Text;

namespace Api.Data;

/// <summary>
/// Copies AdventureWorksLT from SQL Server (restored from Microsoft's .bak by setup.sh)
/// into MySQL and Postgres, so all three databases hold identical rows.
/// Runs at startup only when the target is empty.
/// </summary>
public sealed class Seeder(DbRegistry dbs, IConfiguration config, IHostEnvironment env, ILogger<Seeder> log)
{
    // Parents before children so foreign keys are satisfied.
    static readonly string[] Tables =
    [
        "ProductCategory", "ProductModel", "ProductDescription", "ProductModelProductDescription",
        "Product", "Customer", "Address", "CustomerAddress", "SalesOrderHeader", "SalesOrderDetail",
    ];

    const int BatchSize = 100;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var target in new[] { Db.MySql, Db.Postgres })
        {
            try
            {
                if (await HasDataAsync(target, ct))
                {
                    log.LogInformation("{Db}: already seeded", target);
                    continue;
                }
                await SeedAsync(target, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "{Db}: seeding failed (did you run ./setup.sh?)", target);
            }
        }
    }

    async Task<bool> HasDataAsync(Db db, CancellationToken ct)
    {
        await using var conn = dbs.Admin(db);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = db == Db.MySql
            ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'SalesLT' AND table_name = 'SalesOrderDetail'"
            : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'saleslt' AND table_name = 'salesorderdetail'";
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0) return false;
        cmd.CommandText = "SELECT COUNT(*) FROM SalesLT.SalesOrderDetail";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    async Task SeedAsync(Db target, CancellationToken ct)
    {
        var schemaFile = Path.Combine(env.ContentRootPath, config["SchemaDir"] ?? "../db",
            target == Db.MySql ? "mysql" : "postgres", "schema.sql");
        log.LogInformation("{Db}: creating schema from {File}", target, schemaFile);

        await using var dst = dbs.Admin(target);
        await dst.OpenAsync(ct);
        // The practice login's password comes from config, not from the committed SQL file.
        var practicePassword = (config["PracticePassword"] ?? throw new InvalidOperationException("PracticePassword is not configured"))
            .Replace("'", "''");
        var schemaSql = (await File.ReadAllTextAsync(schemaFile, ct)).Replace("__PRACTICE_PASSWORD__", practicePassword);
        await ExecAsync(dst, schemaSql, ct);
        if (target == Db.MySql) await dst.ChangeDatabaseAsync("SalesLT", ct);

        await using var src = dbs.Admin(Db.SqlServer);
        await src.OpenAsync(ct);

        var columns = new Dictionary<string, List<string>>();
        foreach (var table in Tables) columns[table] = await InsertableColumnsAsync(dst, target, table, ct);

        await using var tx = await dst.BeginTransactionAsync(ct);
        foreach (var table in Tables)
        {
            var copied = await CopyTableAsync(src, dst, tx, table, columns[table], ct);
            log.LogInformation("{Db}: {Table} <- {Rows} rows", target, table, copied);
        }
        if (target == Db.Postgres) await ResetIdentitiesAsync(dst, tx, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Target columns minus generated ones (LineTotal, TotalDue are computed by the DB).</summary>
    static async Task<List<string>> InsertableColumnsAsync(DbConnection conn, Db db, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = db == Db.MySql
            ? $"SELECT column_name FROM information_schema.columns WHERE table_schema = 'SalesLT' AND table_name = '{table}' AND generation_expression = '' ORDER BY ordinal_position"
            : $"SELECT column_name FROM information_schema.columns WHERE table_schema = 'saleslt' AND table_name = '{table.ToLowerInvariant()}' AND is_generated = 'NEVER' ORDER BY ordinal_position";
        var cols = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) cols.Add(r.GetString(0));
        return cols;
    }

    static async Task<int> CopyTableAsync(DbConnection src, DbConnection dst, DbTransaction tx, string table, List<string> columns, CancellationToken ct)
    {
        await using var read = src.CreateCommand();
        // SQL Server identifiers are case-insensitive, so lower-case Postgres names work here too.
        // Parent categories first: ProductCategory references itself.
        read.CommandText = $"SELECT {string.Join(", ", columns)} FROM SalesLT.{table}" +
            (table == "ProductCategory" ? " ORDER BY CASE WHEN ParentProductCategoryID IS NULL THEN 0 ELSE 1 END, ProductCategoryID" : "");
        await using var reader = await read.ExecuteReaderAsync(ct);

        var batch = new List<object?[]>(BatchSize);
        var total = 0;
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[columns.Count];
            for (var i = 0; i < row.Length; i++)
            {
                var v = reader.GetValue(i);
                row[i] = v switch
                {
                    DBNull => null,
                    byte b => (short)b, // tinyint -> smallint
                    _ => v,
                };
            }
            batch.Add(row);
            if (batch.Count == BatchSize)
            {
                total += await InsertBatchAsync(dst, tx, table, columns, batch, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0) total += await InsertBatchAsync(dst, tx, table, columns, batch, ct);
        return total;
    }

    static async Task<int> InsertBatchAsync(DbConnection dst, DbTransaction tx, string table, List<string> columns,
        List<object?[]> rows, CancellationToken ct)
    {
        await using var cmd = dst.CreateCommand();
        cmd.Transaction = tx;
        var sql = new StringBuilder($"INSERT INTO SalesLT.{table} ({string.Join(", ", columns)}) VALUES ");
        for (var r = 0; r < rows.Count; r++)
        {
            sql.Append(r == 0 ? "(" : ", (");
            for (var c = 0; c < columns.Count; c++)
            {
                var name = $"@p{r}_{c}";
                sql.Append(c == 0 ? name : ", " + name);
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = rows[r][c] ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
            sql.Append(')');
        }
        cmd.CommandText = sql.ToString();
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Rows were inserted with explicit ids, so move each identity past the max id.</summary>
    static async Task ResetIdentitiesAsync(DbConnection conn, DbTransaction tx, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT string_agg(format(
              'SELECT setval(pg_get_serial_sequence(%L, %L), (SELECT MAX(%I) FROM %I.%I));',
              table_schema || '.' || table_name, column_name, column_name, table_schema, table_name), ' ')
            FROM information_schema.columns WHERE table_schema = 'saleslt' AND is_identity = 'YES'
            """;
        var script = (string)(await cmd.ExecuteScalarAsync(ct))!;
        await ExecAsync(conn, script, ct, tx);
    }

    static async Task ExecAsync(DbConnection conn, string sql, CancellationToken ct, DbTransaction? tx = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
