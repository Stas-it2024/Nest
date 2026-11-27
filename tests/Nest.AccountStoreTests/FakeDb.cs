using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Nest.Data.Authentication;

namespace AccountStoreTests
{
    internal sealed record Call(string Sql, Dictionary<string, object?> Parameters, Dictionary<string, DbType> Types, bool InTransaction, int Timeout, int Thread);
    internal sealed class FakeDb : DbConnection
    {
        internal readonly List<string> Trace = new();
        internal readonly List<Call> Calls = new();
        internal readonly List<Dictionary<string, object?>> Columns = new();
        internal readonly List<Dictionary<string, object?>> Indexes = new();
        internal readonly List<Dictionary<string, object?>> Checks = new();
        internal Dictionary<string, object?>? Row;
        internal string Mode = "NO_ENGINE_SUBSTITUTION";
        internal string Engine = "InnoDB", Collation = "utf8mb4_general_ci", Charset = "utf8mb4";
        internal long ChecksEnabled = 1, AutoCommit = 1, InTransaction, RowWait = 50, MetadataWait = 86400;
        internal int Affected = 1, Writes, ActiveTransactions;
        internal IsolationLevel Isolation;
        internal bool IgnoreSettings, FailOpen, FailBegin, FailCommit, FailRollback, FailTransactionDispose, FailConnectionDispose, FailClose, FailRestore;
        internal string? ThrowSqlContaining, FailCommandDisposeContaining, FailReaderDisposeContaining;
        internal Action<Call>? OnCommand; internal Exception? TransportException;
        internal int CloseAttempts, CloseSuccesses, DisposeAttempts, DisposeSuccesses, TransactionDisposeAttempts, TransactionDisposeSuccesses;
        internal int CommitAttempts, CommitAcknowledgements, ServerCommittedWrites, PendingWrites;
        internal bool CommitAppliedBeforeError, PendingIsolation;
        internal string BeginFailurePhase = "";
        internal Action? OnOpen, OnBegin, BeforeClose;
        internal bool IsDisposed => DisposeSuccesses > 0;
        internal bool ConnectionReleased => CloseSuccesses > 0 || DisposeSuccesses > 0;
        private ConnectionState state;
        internal static Dictionary<string, object?> D(params (string Key, object? Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        internal FakeDb()
        {
            Col("id", "int(11)", false, null, "auto_increment");
            Col("benutzername", "varchar(50)", false, null, "", true);
            Col("passwort", "varchar(255)", false, null, "", true);
            Col("vollstaendiger_name", "varchar(100)", false, null, "", true);
            Col("rolle", "enum('Admin','Rezeptionist')", false, "'Rezeptionist'", "", true);
            Col("status", "enum('Aktiv','Inaktiv')", false, "'Aktiv'", "", true);
            Col("hinzugefuegt_am", "datetime", true, "current_timestamp()", ""); Col("geloescht_am", "datetime", true, "NULL", "");
            Col("password_format", "tinyint(3) unsigned", false, null, ""); Col("credential_revision", "bigint(20)", false, null, ""); Col("account_revision", "bigint(20)", false, null, "");
            Indexes.Add(D(("INDEX_NAME", "PRIMARY"), ("NON_UNIQUE", 0), ("SEQ_IN_INDEX", 1), ("COLUMN_NAME", "id"), ("SUB_PART", null), ("INDEX_TYPE", "BTREE")));
            Indexes.Add(D(("INDEX_NAME", "benutzername"), ("NON_UNIQUE", 0), ("SEQ_IN_INDEX", 1), ("COLUMN_NAME", "benutzername"), ("SUB_PART", null), ("INDEX_TYPE", "BTREE")));
            Checks.Add(D(("CONSTRAINT_NAME", "ck_benutzer_password_format"), ("CHECK_CLAUSE", "`password_format` in (0,1,2)")));
            Checks.Add(D(("CONSTRAINT_NAME", "ck_benutzer_credential_revision"), ("CHECK_CLAUSE", "`credential_revision` between 0 and 9223372036854775807")));
            Checks.Add(D(("CONSTRAINT_NAME", "ck_benutzer_account_revision"), ("CHECK_CLAUSE", "`account_revision` between 0 and 9223372036854775807")));
            Row = D(("id", 7), ("benutzername", "Synthetic"), ("vollstaendiger_name", "Offline Fixture"), ("rolle", "Admin"), ("status", "Aktiv"),
                ("geloescht_am", null), ("passwort", "synthetic-legacy"), ("password_format", (byte)1), ("credential_revision", 4L), ("account_revision", 9L));
        }
        private void Col(string name, string type, bool nullable, object? defaultValue, string extra, bool text = false) => Columns.Add(D(
            ("COLUMN_NAME", name), ("COLUMN_TYPE", type), ("IS_NULLABLE", nullable ? "YES" : "NO"), ("COLUMN_DEFAULT", defaultValue), ("EXTRA", extra),
            ("CHARACTER_SET_NAME", text ? "utf8mb4" : null), ("COLLATION_NAME", text ? "utf8mb4_general_ci" : null), ("GENERATION_EXPRESSION", "")));
        internal SqlAccountStore Store() => new(() => AccountStoreSession.Open(() => this, c => c.Open()));
        internal Call AccountCall => Calls.Last(c => c.Sql.StartsWith("UPDATE ", StringComparison.Ordinal) || c.Sql.StartsWith("INSERT ", StringComparison.Ordinal));
        internal List<Call> AccountWrites => Calls.Where(c => c.Sql.StartsWith("UPDATE ", StringComparison.Ordinal) || c.Sql.StartsWith("INSERT ", StringComparison.Ordinal)).ToList();
        private Call Record(FakeCommand command)
        {
            var p = command.Parameters.Cast<DbParameter>().ToDictionary(x => x.ParameterName, x => x.Value is DBNull ? null : x.Value);
            var types = command.Parameters.Cast<DbParameter>().ToDictionary(x => x.ParameterName, x => x.DbType);
            var call = new Call(command.CommandText, p, types, command.Transaction != null, command.CommandTimeout, Environment.CurrentManagedThreadId);
            Calls.Add(call); Trace.Add(call.Sql.StartsWith("SET ", StringComparison.Ordinal) ? "set" : call.Sql.Contains("information_schema", StringComparison.Ordinal) ? "schema" : call.Sql.Contains("@@SESSION", StringComparison.Ordinal) ? "state" : "account");
            OnCommand?.Invoke(call);
            if (ThrowSqlContaining != null && call.Sql.Contains(ThrowSqlContaining, StringComparison.Ordinal)) throw TransportException ?? new InvalidOperationException("synthetic-secret-marker");
            return call;
        }
        internal DbDataReader Read(FakeCommand command)
        {
            var c = Record(command);
            List<Dictionary<string, object?>> rows;
            if (c.Sql.Contains("@@SESSION", StringComparison.Ordinal)) rows = new() { D(("sql_mode", Mode), ("checks_enabled", ChecksEnabled), ("autocommit", AutoCommit), ("in_transaction", InTransaction), ("row_wait", RowWait), ("metadata_wait", MetadataWait), ("character_set", Charset)) };
            else if (c.Sql.Contains("information_schema.TABLES", StringComparison.Ordinal)) rows = new() { D(("ENGINE", Engine), ("TABLE_COLLATION", Collation)) };
            else if (c.Sql.Contains("information_schema.COLUMNS", StringComparison.Ordinal)) rows = Columns;
            else if (c.Sql.Contains("information_schema.STATISTICS", StringComparison.Ordinal)) rows = Indexes;
            else if (c.Sql.Contains("information_schema.CHECK_CONSTRAINTS", StringComparison.Ordinal)) rows = Checks;
            else if (c.Sql.Contains("@metadataProbe", StringComparison.Ordinal)) { Trace.Add("metadata-pin"); rows = new(); }
            else rows = Row == null ? new() : new() { new(Row) };
            var table = new DataTable();
            if (rows.Count > 0)
            {
                foreach (var key in rows[0].Keys) table.Columns.Add(key, typeof(object));
                foreach (var row in rows) table.Rows.Add(table.Columns.Cast<DataColumn>().Select(col => row[col.ColumnName] ?? DBNull.Value).ToArray());
            }
            var reader = table.CreateDataReader();
            if (FailReaderDisposeContaining != null && c.Sql.Contains(FailReaderDisposeContaining, StringComparison.Ordinal))
                return new FaultReader(reader);
            return reader;
        }
        internal int Execute(FakeCommand command)
        {
            var call = Record(command);
            if (call.Sql.StartsWith("SET ", StringComparison.Ordinal))
            {
                var newMode = (string)call.Parameters["@mode"]!;
                if (FailRestore && !newMode.Contains("STRICT_ALL_TABLES", StringComparison.Ordinal)) throw new InvalidOperationException("synthetic-secret-marker");
                if (!IgnoreSettings) { Mode = newMode; RowWait = (long)call.Parameters["@row_wait"]!; MetadataWait = (long)call.Parameters["@metadata_wait"]!; }
                return 0;
            }
            Writes++; if (Affected == 1) PendingWrites++; return Affected; // Scripted transport result, NOT an SQL/MariaDB emulator.
        }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline-only";
        public override string DataSource => "fake";
        public override string ServerVersion => "fake";
        public override ConnectionState State => state;
        public override void Open() { Trace.Add("open"); OnOpen?.Invoke(); if (FailOpen) throw new InvalidOperationException("synthetic-secret-marker"); state = ConnectionState.Open; }
        public override void Close()
        {
            CloseAttempts++; Trace.Add("close"); BeforeClose?.Invoke();
            if (FailClose) throw new InvalidOperationException("synthetic-secret-marker");
            CloseSuccesses++; state = ConnectionState.Closed; InTransaction = 0; PendingIsolation = false; PendingWrites = 0;
        }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            Trace.Add("begin"); OnBegin?.Invoke();
            if (FailBegin || BeginFailurePhase == "before") throw new InvalidOperationException("synthetic-secret-marker");
            Trace.Add("set-transaction"); PendingIsolation = true;
            if (BeginFailurePhase == "between") throw new InvalidOperationException("synthetic-secret-marker");
            Trace.Add("begin-sent"); PendingIsolation = false; InTransaction = 1;
            if (BeginFailurePhase == "ack-lost") throw new InvalidOperationException("synthetic-secret-marker");
            Isolation = isolationLevel; ActiveTransactions++; return new FakeTransaction(this, isolationLevel);
        }
        protected override DbCommand CreateDbCommand() => new FakeCommand(this);
        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            DisposeAttempts++; Trace.Add("dispose");
            if (FailConnectionDispose) throw new InvalidOperationException("synthetic-secret-marker");
            DisposeSuccesses++; state = ConnectionState.Closed; InTransaction = 0; PendingIsolation = false; PendingWrites = 0;
        }
        private sealed class FakeTransaction(FakeDb db, IsolationLevel isolation) : DbTransaction
        {
            public override IsolationLevel IsolationLevel => isolation;
            protected override DbConnection DbConnection => db;
            public override void Commit()
            {
                db.Trace.Add("commit"); db.CommitAttempts++;
                if (db.FailCommit && !db.CommitAppliedBeforeError) throw new InvalidOperationException("synthetic-secret-marker");
                db.ServerCommittedWrites += db.PendingWrites; db.PendingWrites = 0; db.InTransaction = 0;
                if (db.FailCommit) throw new InvalidOperationException("synthetic-secret-marker");
                db.CommitAcknowledgements++;
            }
            public override void Rollback() { db.Trace.Add("rollback"); if (db.FailRollback) throw new InvalidOperationException("synthetic-secret-marker"); db.InTransaction = 0; db.PendingWrites = 0; }
            protected override void Dispose(bool disposing) { if (!disposing) return; db.Trace.Add("transaction-dispose"); db.TransactionDisposeAttempts++; if (db.FailTransactionDispose) throw new InvalidOperationException("synthetic-secret-marker"); db.TransactionDisposeSuccesses++; db.ActiveTransactions--; }
        }
        private sealed class FaultReader(DbDataReader inner) : DbDataReader
        {
            public override int Depth => inner.Depth;
            public override int FieldCount => inner.FieldCount;
            public override bool HasRows => inner.HasRows;
            public override bool IsClosed => inner.IsClosed;
            public override int RecordsAffected => inner.RecordsAffected;
            public override object this[int ordinal] => inner[ordinal];
            public override object this[string name] => inner[name];
            public override bool GetBoolean(int i) => inner.GetBoolean(i);
            public override byte GetByte(int i) => inner.GetByte(i);
            public override long GetBytes(int i, long o, byte[]? b, int bo, int l) => inner.GetBytes(i,o,b,bo,l);
            public override char GetChar(int i) => inner.GetChar(i);
            public override long GetChars(int i, long o, char[]? b, int bo, int l) => inner.GetChars(i,o,b,bo,l);
            public override string GetDataTypeName(int i) => inner.GetDataTypeName(i);
            public override DateTime GetDateTime(int i) => inner.GetDateTime(i);
            public override decimal GetDecimal(int i) => inner.GetDecimal(i);
            public override double GetDouble(int i) => inner.GetDouble(i);
            public override Type GetFieldType(int i) => inner.GetFieldType(i);
            public override float GetFloat(int i) => inner.GetFloat(i);
            public override Guid GetGuid(int i) => inner.GetGuid(i);
            public override short GetInt16(int i) => inner.GetInt16(i);
            public override int GetInt32(int i) => inner.GetInt32(i);
            public override long GetInt64(int i) => inner.GetInt64(i);
            public override string GetName(int i) => inner.GetName(i);
            public override int GetOrdinal(string n) => inner.GetOrdinal(n);
            public override string GetString(int i) => inner.GetString(i);
            public override object GetValue(int i) => inner.GetValue(i);
            public override int GetValues(object[] values) => inner.GetValues(values);
            public override bool IsDBNull(int i) => inner.IsDBNull(i);
            public override bool NextResult() => inner.NextResult();
            public override bool Read() => inner.Read();
            public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
            public override void Close() => inner.Close();
            protected override void Dispose(bool disposing) { if (!disposing) return; inner.Dispose(); throw new InvalidOperationException("synthetic-secret-marker"); }
        }
        internal sealed class FakeCommand(FakeDb db) : DbCommand
        {
            private readonly FakeParameters parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = db;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => parameters;
            public override void Cancel() => throw new NotSupportedException();
            public override int ExecuteNonQuery() => db.Execute(this);
            public override object? ExecuteScalar() => throw new NotSupportedException();
            public override void Prepare() => throw new NotSupportedException();
            protected override DbParameter CreateDbParameter() => new FakeParameter();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => db.Read(this);
            protected override void Dispose(bool disposing) { if (!disposing) return; if (db.FailCommandDisposeContaining != null && CommandText.Contains(db.FailCommandDisposeContaining, StringComparison.Ordinal)) throw new InvalidOperationException("synthetic-secret-marker"); }
        }
        private sealed class FakeParameter : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
            public override bool IsNullable { get; set; }
            [AllowNull] public override string ParameterName { get; set; } = "";
            [AllowNull] public override string SourceColumn { get; set; } = "";
            public override object? Value { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override int Size { get; set; }
            public override void ResetDbType() => DbType = DbType.Object;
        }
        private sealed class FakeParameters : DbParameterCollection
        {
            private readonly List<DbParameter> items = new();
            public override int Count => items.Count;
            public override object SyncRoot => this;
            public override int Add(object value) { items.Add((DbParameter)value); return items.Count - 1; }
            public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
            public override void Clear() => items.Clear();
            public override bool Contains(object value) => items.Contains((DbParameter)value);
            public override bool Contains(string value) => IndexOf(value) >= 0;
            public override void CopyTo(Array array, int index) => ((ICollection)items).CopyTo(array, index);
            public override IEnumerator GetEnumerator() => items.GetEnumerator();
            public override int IndexOf(object value) => items.IndexOf((DbParameter)value);
            public override int IndexOf(string parameterName) => items.FindIndex(x => x.ParameterName == parameterName);
            public override void Insert(int index, object value) => items.Insert(index, (DbParameter)value);
            public override void Remove(object value) => items.Remove((DbParameter)value);
            public override void RemoveAt(int index) => items.RemoveAt(index);
            public override void RemoveAt(string parameterName) => items.RemoveAt(IndexOf(parameterName));
            protected override DbParameter GetParameter(int index) => items[index];
            protected override DbParameter GetParameter(string parameterName) => items[IndexOf(parameterName)];
            protected override void SetParameter(int index, DbParameter value) => items[index] = value;
            protected override void SetParameter(string parameterName, DbParameter value) { var index = IndexOf(parameterName); if (index < 0) items.Add(value); else items[index] = value; }
        }
    }
}
