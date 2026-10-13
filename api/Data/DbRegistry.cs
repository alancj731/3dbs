using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace Api.Data;

public enum Db { SqlServer, MySql, Postgres }

/// <summary>Creates connections. "Admin" is used for seeding and schema browsing,
/// "Practice" (data access only, no DDL) runs the SQL typed by the user.</summary>
public sealed class DbRegistry(IConfiguration config)
{
    public DbConnection Admin(Db db) => Create(db, "Admin");
    public DbConnection Practice(Db db) => Create(db, "Practice");

    DbConnection Create(Db db, string role)
    {
        var cs = config.GetConnectionString(db + role)
            ?? throw new InvalidOperationException($"Missing connection string {db}{role}");
        return db switch
        {
            Db.SqlServer => new SqlConnection(cs),
            Db.MySql => new MySqlConnection(cs),
            Db.Postgres => new NpgsqlConnection(cs),
            _ => throw new ArgumentOutOfRangeException(nameof(db)),
        };
    }

    public static bool TryParseDb(string value, out Db db) =>
        Enum.TryParse(value, ignoreCase: true, out db) && Enum.IsDefined(db);
}
