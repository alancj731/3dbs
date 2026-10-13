using Api.Data;
using Api.Practice;

namespace Api.Tests;

public class SqlGuardTests
{
    public static TheoryData<Db> AllDbs => new() { Db.SqlServer, Db.MySql, Db.Postgres };

    static void Allowed(Db db, string sql) => Assert.Null(SqlGuard.Validate(db, sql));
    static void Rejected(Db db, string sql) => Assert.NotNull(SqlGuard.Validate(db, sql));

    [Theory]
    [MemberData(nameof(AllDbs))]
    public void Allows_reads_and_row_changes(Db db)
    {
        Allowed(db, "SELECT * FROM SalesLT.Product");
        Allowed(db, "WITH c AS (SELECT 1 AS x) SELECT x FROM c");
        Allowed(db, "INSERT INTO SalesLT.ProductCategory (Name) VALUES ('Test')");
        Allowed(db, "UPDATE SalesLT.Product SET ListPrice = ListPrice * 1.1");
        Allowed(db, "DELETE FROM SalesLT.ProductCategory WHERE Name = 'Test'");
        Allowed(db, "(SELECT 1) UNION (SELECT 2)");
        Allowed(db, "SELECT 1; SELECT 2;");
        Allowed(db, "select committed, executions from t"); // keywords only match as whole words
    }

    [Theory]
    [MemberData(nameof(AllDbs))]
    public void Rejects_transaction_control_and_dynamic_sql(Db db)
    {
        Rejected(db, "COMMIT");
        Rejected(db, "SELECT 1; ROLLBACK");
        Rejected(db, "SELECT 1 COMMIT"); // T-SQL needs no ';' between statements
        Rejected(db, "BEGIN TRANSACTION");
        Rejected(db, "START TRANSACTION");
        Rejected(db, "SAVEPOINT a");
        Rejected(db, "EXEC('COMMIT')");
        Rejected(db, "EXECUTE sp_executesql N'SELECT 1'");
        Rejected(db, "SELECT 1; xp_cmdshell 'dir'");
        Rejected(db, "PREPARE s FROM 'COMMIT'");
        Rejected(db, "CALL p()");
    }

    [Theory]
    [MemberData(nameof(AllDbs))]
    public void Rejects_statements_outside_the_allow_list(Db db)
    {
        Rejected(db, "DROP TABLE SalesLT.Product");
        Rejected(db, "SELECT 1; TRUNCATE TABLE SalesLT.Product");
        Rejected(db, "CREATE TABLE t (x int)");
        Rejected(db, "ALTER TABLE SalesLT.Product ADD x int");
        Rejected(db, "SET autocommit = 1");
        Rejected(db, "ANALYZE TABLE SalesLT.Product");
    }

    [Theory]
    [MemberData(nameof(AllDbs))]
    public void Ignores_keywords_in_strings_and_comments(Db db)
    {
        Allowed(db, "SELECT 'COMMIT; DROP TABLE x'");
        Allowed(db, "SELECT 'it''s COMMIT'");
        Allowed(db, "SELECT 1 -- COMMIT");
        Allowed(db, "SELECT 1 /* COMMIT */");
        Allowed(db, "SELECT \"COMMIT\"");
    }

    [Theory]
    [MemberData(nameof(AllDbs))]
    public void Rejects_unterminated_strings_and_comments(Db db)
    {
        Rejected(db, "SELECT 'abc");
        Rejected(db, "SELECT \"abc");
        Rejected(db, "SELECT 1 /* COMMIT");
    }

    [Fact]
    public void Follows_each_dialects_comment_rules()
    {
        // MySQL needs whitespace after "--", so "--COMMIT" is code there and a comment elsewhere.
        Rejected(Db.MySql, "SELECT 1--COMMIT");
        Allowed(Db.Postgres, "SELECT 1--COMMIT");
        Allowed(Db.MySql, "SELECT 1--1");

        // '#' starts a comment only on MySQL.
        Allowed(Db.MySql, "SELECT 1 # COMMIT");
        Rejected(Db.Postgres, "SELECT 1 # COMMIT");

        // Block comments nest on SQL Server and Postgres, but not on MySQL.
        Allowed(Db.SqlServer, "SELECT 1 /* a /* b */ COMMIT */");
        Allowed(Db.Postgres, "SELECT 1 /* a /* b */ COMMIT */");
        Rejected(Db.MySql, "SELECT 1 /* a /* b */ COMMIT */");
    }

    [Fact]
    public void Rejects_mysql_executable_comments()
    {
        Rejected(Db.MySql, "SELECT 1 /*! COMMIT */");
        Rejected(Db.MySql, "SELECT /*+ BKA(t) */ 1");
        Allowed(Db.Postgres, "SELECT 1 /*! COMMIT */");
    }

    [Fact]
    public void Follows_each_dialects_quoting_rules()
    {
        Allowed(Db.SqlServer, "SELECT [COMMIT] FROM t");
        Rejected(Db.MySql, "SELECT [COMMIT] FROM t");
        Allowed(Db.MySql, "SELECT `COMMIT` FROM t");
        Rejected(Db.SqlServer, "SELECT `COMMIT` FROM t");

        // Backslash escapes: always in MySQL strings, only in E'' strings on Postgres.
        Allowed(Db.MySql, @"SELECT 'it\'s COMMIT'");
        Allowed(Db.Postgres, @"SELECT E'it\'s COMMIT'");
        Rejected(Db.Postgres, @"SELECT 'it\'s COMMIT'");

        // Postgres dollar quoting.
        Allowed(Db.Postgres, "SELECT $$COMMIT$$");
        Allowed(Db.Postgres, "SELECT $tag$ COMMIT $tag$");
        Rejected(Db.Postgres, "SELECT $tag$ COMMIT");
    }
}
