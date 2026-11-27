using System.Data;
using System.Data.Common;
using MySql.Data.MySqlClient;
using Nest.Security.Authentication;

namespace Nest.Data.Authentication;

// One non-pooled connection per operation. Never shared with another operation.
// Close/Dispose are both attempted; network failure is never a proof of server cleanup.
internal sealed class AccountStoreSession
{
    internal const int CommandTimeoutSeconds = 10;
    internal const int LockTimeoutSeconds = 5;
    private readonly DbConnection connection;
    private string? originalMode;
    private long originalRowWait, originalMetadataWait;
    private bool settingsKnown, settingsTouched, closed;
    internal DbTransaction? Transaction { get; private set; }
    internal bool Unusable { get; set; }

    private AccountStoreSession(DbConnection connection) { this.connection = connection; }
    internal static AccountStoreSession Open(Func<DbConnection> factory, Action<DbConnection> open)
    {
        if (System.Transactions.Transaction.Current != null)
            throw new InvalidOperationException("Account store requires an independent local transaction.");
        var session = new AccountStoreSession(factory());
        try
        {
            if (session.connection is MySqlConnection native)
            {
                var policy = new MySqlConnectionStringBuilder(native.ConnectionString);
                AccountStoreSchemaGuard.Require(!policy.Pooling && policy.DefaultCommandTimeout == CommandTimeoutSeconds && !policy.UseAffectedRows);
            }
            open(session.connection);
            var state = session.ReadState();
            session.originalMode = AccountStoreSchemaGuard.Text(state, "sql_mode") ?? throw new InvalidOperationException();
            session.originalRowWait = AccountStoreSchemaGuard.Number(state, "row_wait");
            session.originalMetadataWait = AccountStoreSchemaGuard.Number(state, "metadata_wait");
            session.settingsKnown = true;
            AccountStoreSchemaGuard.Require(AccountStoreSchemaGuard.Number(state, "autocommit") == 1 &&
                AccountStoreSchemaGuard.Number(state, "in_transaction") == 0 && AccountStoreSchemaGuard.Number(state, "checks_enabled") == 1 &&
                AccountStoreSchemaGuard.Text(state, "character_set") == "utf8mb4");
            var modes = session.originalMode.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var strict = modes.Contains("STRICT_ALL_TABLES", StringComparer.OrdinalIgnoreCase) ? session.originalMode :
                string.Join(',', modes.Append("STRICT_ALL_TABLES"));
            session.settingsTouched = true; // Even a failed SET leaves session state unverified; skip restoration on uncertain connections.
            session.Execute("SET SESSION sql_mode=@mode, innodb_lock_wait_timeout=@row_wait, lock_wait_timeout=@metadata_wait",
                ("@mode", DbType.String, strict), ("@row_wait", DbType.Int64, (long)LockTimeoutSeconds), ("@metadata_wait", DbType.Int64, (long)LockTimeoutSeconds));
            var verified = session.ReadState();
            AccountStoreSchemaGuard.Require(AccountStoreSchemaGuard.Text(verified, "sql_mode")!.Split(',').Contains("STRICT_ALL_TABLES", StringComparer.OrdinalIgnoreCase) &&
                AccountStoreSchemaGuard.Text(verified, "sql_mode")!.Split(',').ToHashSet(StringComparer.OrdinalIgnoreCase).IsSupersetOf(modes) &&
                AccountStoreSchemaGuard.Number(verified, "checks_enabled") == 1 && AccountStoreSchemaGuard.Number(verified, "autocommit") == 1 &&
                AccountStoreSchemaGuard.Number(verified, "in_transaction") == 0 &&
                AccountStoreSchemaGuard.Number(verified, "row_wait") == LockTimeoutSeconds && AccountStoreSchemaGuard.Number(verified, "metadata_wait") == LockTimeoutSeconds);
            AccountStoreSchemaGuard.Verify(session);
            return session;
        }
        catch { session.Unusable = true; session.Close(); throw new InvalidOperationException("Account store connection could not be prepared."); }
    }
    private Dictionary<string, object?> ReadState()
    {
        var rows = Read("SELECT @@SESSION.sql_mode AS sql_mode, @@SESSION.check_constraint_checks AS checks_enabled, @@SESSION.autocommit AS autocommit, @@SESSION.in_transaction AS in_transaction, @@SESSION.innodb_lock_wait_timeout AS row_wait, @@SESSION.lock_wait_timeout AS metadata_wait, @@SESSION.character_set_connection AS character_set");
        AccountStoreSchemaGuard.Require(rows.Count == 1); return rows[0];
    }
    private DbCommand Command(string sql, (string Name, DbType Type, object? Value)[] args)
    {
        var command = connection.CreateCommand();
        try
        {
            command.CommandText = sql; command.CommandTimeout = CommandTimeoutSeconds; command.Transaction = Transaction;
            foreach (var arg in args)
            {
                var parameter = command.CreateParameter(); parameter.ParameterName = arg.Name; parameter.DbType = arg.Type;
                parameter.Value = arg.Value ?? DBNull.Value; command.Parameters.Add(parameter);
            }
            return command;
        }
        catch { command.Dispose(); throw; }
    }
    internal List<Dictionary<string, object?>> Read(string sql, params (string Name, DbType Type, object? Value)[] args)
    {
        using var command = Command(sql, args);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            if (rows.Count >= 64) throw new InvalidOperationException("Unexpected account store result cardinality.");
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++) row.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
            rows.Add(row);
        }
        return rows;
    }
    internal int Execute(string sql, params (string Name, DbType Type, object? Value)[] args)
    { using var command = Command(sql, args); return command.ExecuteNonQuery(); }
    internal void Begin()
    {
        try
        {
            Transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            // Touch the actual table before revalidating metadata. Its transactional MDL
            // is retained until COMMIT/ROLLBACK, including for an empty result/table.
            Read("SELECT id FROM benutzer WHERE id=@metadataProbe", ("@metadataProbe", DbType.Int32, 0));
            AccountStoreSchemaGuard.Verify(this);
        }
        catch { Unusable = true; throw; }
    }
    internal void Commit() => Transaction!.Commit();
    internal void Rollback() => Transaction!.Rollback();
    internal bool DisposeTransaction()
    {
        var transaction = Transaction; Transaction = null;
        try { transaction?.Dispose(); return true; } catch { Unusable = true; return false; }
    }
    // Returns cleanup failure; never throws or changes the established transactional outcome.
    internal bool Close()
    {
        if (closed) return false; closed = true;
        var failed = false;
        if (Transaction != null) { Unusable = true; failed |= !DisposeTransaction(); }
        if (settingsTouched && settingsKnown && !Unusable)
        {
            try
            {
                Execute("SET SESSION sql_mode=@mode, innodb_lock_wait_timeout=@row_wait, lock_wait_timeout=@metadata_wait",
                    ("@mode", DbType.String, originalMode), ("@row_wait", DbType.Int64, originalRowWait), ("@metadata_wait", DbType.Int64, originalMetadataWait));
                var state = ReadState();
                if (AccountStoreSchemaGuard.Text(state, "sql_mode") != originalMode ||
                    AccountStoreSchemaGuard.Number(state, "row_wait") != originalRowWait || AccountStoreSchemaGuard.Number(state, "metadata_wait") != originalMetadataWait ||
                    AccountStoreSchemaGuard.Number(state, "in_transaction") != 0) throw new InvalidOperationException();
            }
            catch { failed = true; Unusable = true; }
        }
        try { connection.Close(); } catch { failed = true; }
        try { connection.Dispose(); } catch { failed = true; }
        return failed;
    }
    internal static AccountStoreSession OpenDefault() => Open(
        () => DB.GetAccountStoreConnection(), c => DB.OpenConnection((MySqlConnection)c));
}
