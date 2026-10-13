using System.Text;
using System.Text.RegularExpressions;
using Api.Data;

namespace Api.Practice;

/// <summary>
/// Decides whether visitors' SQL may run. The rollback only protects the data while the transaction stays open,
/// so anything that can end it early must be refused: COMMIT/ROLLBACK, dynamic SQL that hides a COMMIT
/// (EXEC('COMMIT'), PREPARE … FROM 'COMMIT'), and MySQL statements that commit implicitly (ANALYZE/OPTIMIZE TABLE…).
/// Rather than list every bad statement, only SELECT/WITH/INSERT/UPDATE/DELETE statements are allowed.
/// The SQL is tokenized per dialect first, so keywords inside strings, comments and quoted names are ignored
/// and comment/quote tricks can't hide a keyword from the check.
/// </summary>
public static partial class SqlGuard
{
    static readonly HashSet<string> AllowedStarts = new(StringComparer.OrdinalIgnoreCase)
        { "SELECT", "WITH", "INSERT", "UPDATE", "DELETE" };

    /// <summary>Returns an error message, or null when the SQL may run.</summary>
    public static string? Validate(Db db, string sql)
    {
        if (!TryStrip(db, sql, out var code, out var lexError)) return lexError;

        if (Forbidden().Match(code) is { Success: true } m)
            return $"\"{m.Value.Trim()}\" isn't allowed here. Every run is wrapped in a transaction that is rolled back, " +
                   "so transaction control, dynamic SQL and procedure calls are disabled.";

        foreach (var statement in code.Split(';'))
        {
            var first = FirstWord().Match(statement);
            if (!first.Success) continue; // empty statement
            if (!AllowedStarts.Contains(first.Groups[1].Value))
                return $"Only SELECT, WITH, INSERT, UPDATE and DELETE statements can be run here (found \"{first.Groups[1].Value}\").";
        }
        return null;
    }

    // Anywhere in the code, not just at a statement start: T-SQL doesn't need ';' between statements.
    [GeneratedRegex(@"\b(COMMIT|ROLLBACK|SAVEPOINT|SAVE\s+TRAN\w*|BEGIN\s+(TRAN\w*|WORK|DISTRIBUTED)|START\s+TRANSACTION|AUTOCOMMIT|IMPLICIT_TRANSACTIONS|EXEC|EXECUTE|PREPARE|CALL|[sx]p_\w+)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    // First word of a statement, skipping opening parentheses: "(SELECT …) UNION …".
    [GeneratedRegex(@"^[\s(]*([A-Za-z_]\w*|\S)")]
    private static partial Regex FirstWord();

    /// <summary>
    /// Replaces string literals, quoted identifiers and comments with a space, the way the given database reads them.
    /// Fails on MySQL executable comments (/*! … */, which MySQL runs) and on unterminated literals.
    /// </summary>
    static bool TryStrip(Db db, string sql, out string code, out string? error)
    {
        var sb = new StringBuilder(sql.Length);
        error = null;
        int i = 0, n = sql.Length;
        bool mysql = db == Db.MySql, postgres = db == Db.Postgres, sqlServer = db == Db.SqlServer;

        while (i < n)
        {
            char c = sql[i];
            char next = i + 1 < n ? sql[i + 1] : '\0';

            // Line comments. MySQL needs whitespace after "--" ("1--1" is arithmetic), and also has '#'.
            if ((c == '-' && next == '-' && (!mysql || i + 2 >= n || char.IsWhiteSpace(sql[i + 2]) || char.IsControl(sql[i + 2])))
                || (mysql && c == '#'))
            {
                while (i < n && sql[i] != '\n') i++;
                sb.Append(' ');
                continue;
            }

            // Block comments: nested on SQL Server and Postgres, not on MySQL.
            if (c == '/' && next == '*')
            {
                if (mysql && i + 2 < n && (sql[i + 2] == '!' || sql[i + 2] == '+'))
                    return Fail("MySQL /*! … */ and /*+ … */ comments aren't allowed here.", out code, out error);
                int depth = 1;
                i += 2;
                while (i < n && depth > 0)
                {
                    if (sql[i] == '*' && i + 1 < n && sql[i + 1] == '/') { depth--; i += 2; }
                    else if (!mysql && sql[i] == '/' && i + 1 < n && sql[i + 1] == '*') { depth++; i += 2; }
                    else i++;
                }
                if (depth > 0) return Fail("Unterminated /* comment.", out code, out error);
                sb.Append(' ');
                continue;
            }

            // Postgres dollar quoting: $$ … $$ or $tag$ … $tag$ (not $1 parameters or names containing $).
            if (postgres && c == '$' && (i == 0 || !IsIdentChar(sql[i - 1])))
            {
                int j = i + 1;
                while (j < n && (char.IsLetter(sql[j]) || sql[j] == '_' || (j > i + 1 && char.IsDigit(sql[j])))) j++;
                if (j < n && sql[j] == '$')
                {
                    var tag = sql[i..(j + 1)];
                    int end = sql.IndexOf(tag, j + 1, StringComparison.Ordinal);
                    if (end < 0) return Fail("Unterminated $-quoted string.", out code, out error);
                    i = end + tag.Length;
                    sb.Append(' ');
                    continue;
                }
            }

            // Strings and quoted identifiers.
            char close = c switch
            {
                '\'' => '\'',
                '"' => '"',
                '`' when mysql => '`',
                '[' when sqlServer => ']',
                _ => '\0',
            };
            if (close != '\0')
            {
                // Backslash escapes: MySQL strings ('…' and "…"), and Postgres E'…' strings.
                bool backslash = (mysql && c != '`')
                    || (postgres && c == '\'' && i > 0 && (sql[i - 1] is 'E' or 'e') && (i < 2 || !IsIdentChar(sql[i - 2])));
                int j = i + 1;
                bool closed = false;
                while (j < n)
                {
                    if (backslash && sql[j] == '\\') { j += 2; continue; }
                    if (sql[j] == close)
                    {
                        if (j + 1 < n && sql[j + 1] == close) { j += 2; continue; } // doubled: '' "" `` ]]
                        closed = true;
                        j++;
                        break;
                    }
                    j++;
                }
                if (!closed) return Fail($"Unterminated {c} quote.", out code, out error);
                i = j;
                sb.Append(" _ ");
                continue;
            }

            sb.Append(c);
            i++;
        }

        code = sb.ToString();
        return true;
    }

    static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    static bool Fail(string message, out string code, out string? error)
    {
        code = "";
        error = message;
        return false;
    }
}
