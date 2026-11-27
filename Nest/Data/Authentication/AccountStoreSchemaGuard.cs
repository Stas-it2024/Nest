using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nest.Data.Authentication;

// Metadata only. No account rows, DDL, or best-effort acceptance of an unknown schema.
internal static class AccountStoreSchemaGuard
{
    internal static void Verify(AccountStoreSession session)
    {
        var tables = session.Read("SELECT ENGINE, TABLE_COLLATION FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='benutzer' AND TABLE_TYPE='BASE TABLE'");
        Require(tables.Count == 1 && Text(tables[0], "ENGINE") == "InnoDB" && Text(tables[0], "TABLE_COLLATION") == "utf8mb4_general_ci");
        var columns = session.Read("SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, EXTRA, CHARACTER_SET_NAME, COLLATION_NAME, GENERATION_EXPRESSION FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='benutzer'");
        Require(columns.Count == 11);
        Column(columns, "id", "int(11)", false, null, "auto_increment");
        Column(columns, "benutzername", "varchar(50)", false, null, "", true);
        Column(columns, "passwort", "varchar(255)", false, null, "", true);
        Column(columns, "vollstaendiger_name", "varchar(100)", false, null, "", true);
        Column(columns, "rolle", "enum('Admin','Rezeptionist')", false, "Rezeptionist", "", true);
        Column(columns, "status", "enum('Aktiv','Inaktiv')", false, "Aktiv", "", true);
        Column(columns, "hinzugefuegt_am", "datetime", true, "current_timestamp()", "");
        Column(columns, "geloescht_am", "datetime", true, null, "");
        Column(columns, "password_format", "tinyint(3) unsigned", false, null, "");
        Column(columns, "credential_revision", "bigint(20)", false, null, "");
        Column(columns, "account_revision", "bigint(20)", false, null, "");
        var indexes = session.Read("SELECT INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME, SUB_PART, INDEX_TYPE FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='benutzer'");
        Require(indexes.Count == 2);
        Index(indexes, "PRIMARY", "id"); Index(indexes, "benutzername", "benutzername");
        var checks = session.Read("SELECT CONSTRAINT_NAME, CHECK_CLAUSE FROM information_schema.CHECK_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='benutzer'");
        Require(checks.Count == 3);
        Check(checks, "ck_benutzer_password_format", "password_formatin0,1,2");
        Check(checks, "ck_benutzer_credential_revision", "credential_revisionbetween0and9223372036854775807");
        Check(checks, "ck_benutzer_account_revision", "account_revisionbetween0and9223372036854775807");
    }
    private static void Column(List<Dictionary<string, object?>> rows, string name, string type, bool nullable, string? defaultValue, string extra, bool text = false)
    {
        var matches = rows.Where(r => Text(r, "COLUMN_NAME") == name).ToArray(); Require(matches.Length == 1);
        var row = matches[0];
        Require(Text(row, "COLUMN_TYPE") == type && Text(row, "IS_NULLABLE") == (nullable ? "YES" : "NO") &&
            Text(row, "EXTRA") == extra && string.IsNullOrEmpty(Text(row, "GENERATION_EXPRESSION")));
        var actual = Text(row, "COLUMN_DEFAULT");
        Require(defaultValue is null ? actual is null || (nullable && actual == "NULL") :
            actual == defaultValue || actual == "'" + defaultValue + "'");
        if (text) Require(Text(row, "CHARACTER_SET_NAME") == "utf8mb4" && Text(row, "COLLATION_NAME") == "utf8mb4_general_ci");
    }
    private static void Index(List<Dictionary<string, object?>> rows, string name, string column)
    {
        var matches = rows.Where(r => Text(r, "INDEX_NAME") == name).ToArray(); Require(matches.Length == 1);
        var r = matches[0];
        Require(Number(r, "NON_UNIQUE") == 0 && Number(r, "SEQ_IN_INDEX") == 1 && Text(r, "COLUMN_NAME") == column &&
            r["SUB_PART"] is null && Text(r, "INDEX_TYPE") == "BTREE");
    }
    private static void Check(List<Dictionary<string, object?>> rows, string name, string expected)
    {
        var matches = rows.Where(r => Text(r, "CONSTRAINT_NAME") == name).ToArray(); Require(matches.Length == 1);
        var expression = Text(matches[0], "CHECK_CLAUSE"); Require(expression is { Length: <= 512 });
        // MariaDB adds quoting/parentheses/spacing to these fixed, simple expressions.
        // No substring matching, OR, functions, arithmetic, comments or additional terms accepted.
        var canonical = Regex.Replace(expression!, @"[\s`()]+", "", RegexOptions.CultureInvariant).ToLowerInvariant();
        Require(canonical == expected);
    }
    internal static string? Text(Dictionary<string, object?> row, string key) => row[key] is null ? null : (string)row[key]!;
    internal static long Number(Dictionary<string, object?> row, string key) => row[key] switch { byte v => v, sbyte v => v, short v => v, ushort v => v, int v => v, uint v => v, long v => v, ulong v when v <= long.MaxValue => (long)v, _ => throw new InvalidOperationException("Invalid account numeric value.") };
    internal static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Account store configuration is incompatible."); }
}
