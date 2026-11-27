using Nest.Data.Authentication;
using Nest.Security.Authentication;
using static AccountStoreTests.Program;

namespace AccountStoreTests;
internal static class TransactionChecks
{
    internal static async Task Run()
    {
        await Scenario("connector duplicate-key classification uses real exception type", async () =>
        {
            var constructor = typeof(MySql.Data.MySqlClient.MySqlException).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null, new[] { typeof(string), typeof(int), typeof(Exception) }, null);
            Check(constructor != null, "connector exception construction fixture");
            var duplicate = (Exception)constructor!.Invoke(new object?[] { "synthetic-secret-marker", 1062, null });
            var db = new FakeDb { ThrowSqlContaining = "INSERT INTO benutzer", TransportException = duplicate };
            var result = await db.Store().CreateAsync(Profile, Credential);
            Check(result.Outcome == StoreOutcome.Rejected && db.Trace.Contains("rollback") && !db.Trace.Contains("commit"));
            Check(db.AccountWrites.Count == 1 && !result.ToString().Contains("synthetic-secret-marker", StringComparison.Ordinal)); Closed(db);
        });
        await Scenario("cancelled authentication preserves confirmed finalization", async () =>
        {
            var db = new FakeDb(); db.Row!["passwort"] = Credential.Value;
            using var cts = new CancellationTokenSource();
            db.OnCommand = c => { if (c.Sql.Contains("FOR UPDATE", StringComparison.Ordinal)) cts.Cancel(); };
            var result = await new AuthenticationService(db.Store()).AuthenticateAsync(new("Synthetic", "synthetic-only-password"), cts.Token);
            Check(result.Outcome == AuthenticationOutcome.Cancelled && result.FinalizationOutcome == StoreOutcome.Success && !result.IsAuthenticated);
            Check(db.Writes == 0 && db.Trace.Count(x => x == "commit") == 1); Closed(db);
        });
        foreach (var kind in new[] { "finalize-no-rehash", "finalize-rehash", "create", "profile", "password", "combined", "delete" })
        await Scenario("commit uncertainty reaches caller for " + kind, async () =>
        {
            var db = new FakeDb { FailCommit = true }; var store = db.Store();
            var outcome = kind switch
            {
                "finalize-no-rehash" => (await store.FinalizeAsync(AuthStamp, null)).Outcome,
                "finalize-rehash" => (await store.FinalizeAsync(AuthStamp, Credential)).Outcome,
                "create" => (await store.CreateAsync(Profile, Credential)).Outcome,
                "profile" => (await store.UpdateProfileAsync(Stamp, Profile)).Outcome,
                "password" => (await store.ChangeCredentialAsync(Stamp, Credential)).Outcome,
                "combined" => (await store.UpdateProfileAndCredentialAsync(Stamp, Profile, Credential)).Outcome,
                _ => (await store.SoftDeleteAsync(Stamp)).Outcome
            };
            Check(outcome == StoreOutcome.CommitUncertain && db.Trace.Count(x => x == "commit") == 1 && db.AccountWrites.Count <= 1);
            Check(db.ConnectionReleased || (db.FailClose && db.FailConnectionDispose)); Closed(db);
        });
        foreach (var phase in new[] { "before", "between", "ack-lost" })
        await Scenario("partial begin phase " + phase, async () =>
        {
            var db = new FakeDb { BeginFailurePhase = phase };
            bool pendingObserved = false, activeObserved = false;
            db.BeforeClose = () => { pendingObserved = db.PendingIsolation; activeObserved = db.InTransaction != 0; };
            Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.TechnicalFailure && db.Writes == 0);
            Check(pendingObserved == (phase == "between") && activeObserved == (phase == "ack-lost"));
            Check(!db.Trace.Contains("rollback") && db.ConnectionReleased); Closed(db);
        });
        foreach (var applied in new[] { false, true })
        await Scenario("unknown commit has independent server application state", async () =>
        {
            var db = new FakeDb { FailCommit = true, CommitAppliedBeforeError = applied };
            var result = await db.Store().UpdateProfileAndCredentialAsync(Stamp, Profile, Credential);
            Check(result.Outcome == StoreOutcome.CommitUncertain && db.CommitAcknowledgements == 0);
            Check(db.ServerCommittedWrites == (applied ? 1 : 0) && db.CommitAttempts == 1 && db.Writes == 1); Closed(db);
        });
        foreach (var count in new[] { 0, 1, 2, -1 })
        await Scenario("affected rows " + count, async () =>
        {
            var db = new FakeDb { Affected = count };
            var result = await db.Store().UpdateProfileAsync(Stamp, Profile);
            Check(result.Outcome == (count == 1 ? StoreOutcome.Success : count == 0 ? StoreOutcome.ConcurrencyConflict : StoreOutcome.TechnicalFailure));
            Check(db.Writes == 1 && db.Trace.Count(x => x == "commit") == (count == 1 ? 1 : 0));
            Check(db.Trace.Count(x => x == "rollback") == (count == 1 ? 0 : 1)); Closed(db);
        });
        foreach (var kind in new[] { "commit", "commit-and-rollback", "rollback", "command", "begin", "open", "command-dispose", "reader-dispose" })
        await Scenario("failure boundaries " + kind, async () =>
        {
            var db = new FakeDb();
            switch (kind)
            {
                case "commit": db.FailCommit = true; break;
                case "commit-and-rollback": db.FailCommit = true; db.FailRollback = true; break;
                case "rollback": db.FailRollback = true; db.Affected = 0; break;
                case "command": db.ThrowSqlContaining = "UPDATE benutzer"; break;
                case "begin": db.FailBegin = true; break;
                case "open": db.FailOpen = true; break;
                case "command-dispose": db.FailCommandDisposeContaining = "UPDATE benutzer"; break;
                case "reader-dispose": db.FailReaderDisposeContaining = "FOR UPDATE"; break;
            }
            var result = await db.Store().UpdateProfileAsync(Stamp, Profile);
            var expected = kind.StartsWith("commit", StringComparison.Ordinal) ? StoreOutcome.CommitUncertain : kind == "rollback" ? StoreOutcome.UncertainOutcome : StoreOutcome.TechnicalFailure;
            Check(result.Outcome == expected); Check(db.AccountWrites.Count <= 1, "never retry a write");
            if (kind.StartsWith("commit", StringComparison.Ordinal) || kind == "rollback") Check(db.ConnectionReleased, "fake non-pooled transport closed");
            if (kind == "command-dispose") Check(db.Trace.Contains("rollback") && !db.Trace.Contains("commit"));
            Check(!result.ToString().Contains("synthetic-secret-marker", StringComparison.Ordinal)); Closed(db);
        });
        foreach (var kind in new[] { "transaction-dispose", "connection-dispose", "close", "restore", "close-and-dispose" })
        await Scenario("acknowledged commit survives " + kind, async () =>
        {
            var db = new FakeDb();
            switch (kind)
            {
                case "transaction-dispose": db.FailTransactionDispose = true; break;
                case "connection-dispose": db.FailConnectionDispose = true; break;
                case "close": db.FailClose = true; break;
                case "restore": db.FailRestore = true; break;
                default: db.FailClose = true; db.FailConnectionDispose = true; break;
            }
            var result = await db.Store().FinalizeAsync(AuthStamp, Credential);
            Check(result.Outcome == StoreOutcome.CommittedWithCleanupFailure && result.User?.Id == 7);
            Check(db.Trace.Count(x => x == "commit") == 1 && !db.Trace.Contains("rollback") && db.Writes == 1);
            Check(db.ConnectionReleased || (db.FailClose && db.FailConnectionDispose)); Closed(db);
        });
        await Scenario("known refusal stays refusal despite cleanup failure", async () =>
        {
            var db = new FakeDb { Affected = 0, FailConnectionDispose = true };
            Check((await db.Store().UpdateProfileAsync(Stamp, Profile)).Outcome == StoreOutcome.ConcurrencyConflict);
            Check(db.Trace.Contains("rollback") && !db.Trace.Contains("commit")); Closed(db);
        });
        foreach (var kind in new[] { "checks-off", "autocommit-off", "preexisting-transaction", "wrong-charset", "settings-not-applied", "verification-failed" })
        await Scenario("session admission refuses " + kind, async () =>
        {
            var db = new FakeDb();
            switch (kind)
            {
                case "checks-off": db.ChecksEnabled = 0; break;
                case "autocommit-off": db.AutoCommit = 0; break;
                case "preexisting-transaction": db.InTransaction = 1; break;
                case "wrong-charset": db.Charset = "latin1"; break;
                case "settings-not-applied": db.IgnoreSettings = true; break;
                default: var states = 0; db.OnCommand = c => { if (c.Sql.Contains("@@SESSION", StringComparison.Ordinal) && ++states == 2) throw new InvalidOperationException("synthetic-secret-marker"); }; break;
            }
            Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.TechnicalFailure);
            Check(db.Writes == 0 && !db.Trace.Contains("begin") && db.ConnectionReleased || (db.FailClose && db.FailConnectionDispose)); Closed(db);
        });
        await Scenario("cancel before read admission", async () =>
        {
            var db = new FakeDb(); using var cts = new CancellationTokenSource(); cts.Cancel();
            var error = await Error(async () => { await db.Store().FindAsync("synthetic", cts.Token); });
            Check(error is OperationCanceledException && db.Trace.Count == 0);
        });
        await Scenario("cancel during read still cleans connection", async () =>
        {
            var db = new FakeDb(); using var cts = new CancellationTokenSource();
            db.OnCommand = c => { if (c.Sql.StartsWith("SELECT id", StringComparison.Ordinal)) cts.Cancel(); };
            var error = await Error(async () => { await db.Store().FindAsync("synthetic", cts.Token); });
            Check(error is OperationCanceledException && db.Writes == 0); Closed(db);
        });
        await Scenario("service cancellation before write", async () =>
        {
            var db = new FakeDb(); using var cts = new CancellationTokenSource(); cts.Cancel();
            var result = await new UserManagementService(db.Store()).UpdateProfileAsync(Stamp, Profile, cts.Token);
            Check(result.Outcome == StoreOutcome.Cancelled && db.Trace.Count == 0);
        });
        await Scenario("service cancellation after admission cannot assert rollback", async () =>
        {
            var db = new FakeDb(); using var cts = new CancellationTokenSource();
            db.OnCommand = c => { if (c.Sql.StartsWith("UPDATE ", StringComparison.Ordinal)) cts.Cancel(); };
            var result = await new UserManagementService(db.Store()).UpdateProfileAsync(Stamp, Profile, cts.Token);
            Check(cts.IsCancellationRequested && result.Outcome == StoreOutcome.Success && db.Writes == 1 && db.Trace.Contains("commit")); Closed(db);
        });
        await Scenario("lookup transport exception is sanitized", async () =>
        {
            var db = new FakeDb { ThrowSqlContaining = "SELECT id" };
            var ex = await Error(async () => { await db.Store().FindAsync("synthetic", default); });
            Check(!ex.ToString().Contains("synthetic-secret-marker", StringComparison.Ordinal) && ex.InnerException == null); Closed(db);
        });
        await Scenario("lookup cleanup exception is sanitized", async () =>
        {
            var db = new FakeDb { FailRestore = true };
            var ex = await Error(async () => { await db.Store().FindAsync("synthetic", default); });
            Check(!ex.ToString().Contains("synthetic-secret-marker", StringComparison.Ordinal)); Check(db.ConnectionReleased || (db.FailClose && db.FailConnectionDispose)); Closed(db);
        });
        await Scenario("SQL executes off a dedicated caller thread", () => CallerProbe(false));
        await Scenario("failed caller assertion still releases and drains worker", async () =>
        {
            var error = await Error(() => CallerProbe(true, true)); Check(error.Message == "synthetic primary assertion");
            Check(error.Data["ParticipantFailureType"] as string == nameof(InvalidOperationException), "secondary participant failure observed without replacing primary");
            Check(AccountSqlAdmission.Snapshot == (0, 0));
        });
        await Scenario("PBKDF2 is never performed inside store transaction", async () =>
        {
            var db = new FakeDb(); var observed = 0;
            void Watch(PasswordWorkObservation observation) { if (observation.Started) Interlocked.Increment(ref observed); }
            PasswordWorkLimiter.WorkObserved += Watch;
            try { Check((await db.Store().UpdateProfileAndCredentialAsync(Stamp, Profile, Credential)).Outcome == StoreOutcome.Success); Check(observed == 0); }
            finally { PasswordWorkLimiter.WorkObserved -= Watch; }
            Closed(db);
        });        await Scenario("ambient transaction is refused before connection creation", async () =>
        {
            var db = new FakeDb();
            using var scope = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
            Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.TechnicalFailure && db.Trace.Count == 0);
        });
        await LimiterChecks();

    }
    private static async Task CallerProbe(bool fail, bool failParticipant = false)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var db = new FakeDb(); var caller = 0; UserWriteResult? result = null; Exception? failure = null;
        db.OnOpen = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        var thread = new Thread(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            try { result = db.Store().CreateAsync(Profile, Credential).GetAwaiter().GetResult(); if (failParticipant) throw new InvalidOperationException("synthetic participant failure"); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.Start();
        Exception? primary = null;
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); if (fail) throw new InvalidOperationException("synthetic primary assertion"); }
        catch (Exception error) { primary = error; }
        finally { release.Set(); Check(thread.Join(TimeSpan.FromSeconds(20)), "caller and worker drained in finally"); }
        if (primary != null)
        {
            if (failure != null) primary.Data["ParticipantFailureType"] = failure.GetType().Name;
            Closed(db); System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
        Check(failure == null && result?.Outcome == StoreOutcome.Success && db.Calls.All(c => c.Thread != caller)); Closed(db);
    }
    private static async Task LimiterChecks()
    {
        await Scenario("SQL admission lease cannot be fabricated", () =>
        {
            var rejected = false;
            try { using var invalid = new AccountSqlAdmission.Lease(new object()); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && AccountSqlAdmission.Snapshot == (0, 0));
            return Task.CompletedTask;
        });
        await Scenario("admission remains owned until cleanup really finishes", async () =>
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = new List<Task>(); int cleanupEntered = 0;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var db = new FakeDb();
                    db.BeforeClose = () => { if (Interlocked.Increment(ref cleanupEntered) == 4) entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); };
                    tasks.Add(db.Store().CreateAsync(Profile, Credential));
                }
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var waiting = new FakeDb(); tasks.Add(waiting.Store().CreateAsync(Profile, Credential));
                Check(AccountSqlAdmission.Snapshot == (4, 1) && waiting.Trace.Count == 0 && tasks.All(t => !t.IsCompleted));
            }
            finally { release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30)); }
            Check(AccountSqlAdmission.Snapshot == (0, 0));
        });
        await Scenario("shared SQL admission bounds active work and queue and recovers", async () =>
        {
            using var release = new ManualResetEventSlim(); using var queuedCancel = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var participants = new List<Task>(); var fixtures = new List<FakeDb>(); int opened = 0;
            FakeDb Fixture(bool hold)
            {
                var db = new FakeDb(); fixtures.Add(db);
                if (hold) db.OnOpen = () => { if (Interlocked.Increment(ref opened) == 4) entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); };
                return db;
            }
            try
            {
                for (int i = 0; i < 4; i++) participants.Add(Fixture(true).Store().CreateAsync(Profile, Credential));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Check(AccountSqlAdmission.Snapshot == (4, 0));
                var cancelledDb = Fixture(false); var cancelled = cancelledDb.Store().FindAsync("Synthetic", queuedCancel.Token); participants.Add(cancelled);
                for (int i = 1; i < 16; i++) participants.Add(Fixture(false).Store().CreateAsync(Profile, Credential));
                Check(AccountSqlAdmission.Snapshot == (4, 16) && fixtures.Skip(4).All(d => d.Trace.Count == 0), "queue allocates no connection");
                var overflow = Fixture(false); Check((await overflow.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.TechnicalFailure && overflow.Trace.Count == 0);
                var readOverflow = Fixture(false); var error = await Error(async () => { await readOverflow.Store().FindAsync("Synthetic", default); });
                Check(error is InvalidOperationException && readOverflow.Trace.Count == 0, "overload cannot masquerade as an absent username");
                var busyAuthentication = await new AuthenticationService(Fixture(false).Store()).AuthenticateAsync(new("Synthetic", "synthetic-only-password"));
                Check(busyAuthentication.Outcome == AuthenticationOutcome.TechnicalFailure && !busyAuthentication.IsAuthenticated, "overload is technical failure, not bad credentials");
                queuedCancel.Cancel(); await Error(async () => await cancelled);
                Check(cancelled.IsCanceled && AccountSqlAdmission.Snapshot == (4, 15) && cancelledDb.Trace.Count == 0);
            }
            finally
            {
                release.Set();
                // WhenAll observes every participant; all synthetic blocking points are bounded.
                try { await Task.WhenAll(participants).WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (OperationCanceledException) when (participants.All(t => t.IsCompleted)) { }
            }
            Check(AccountSqlAdmission.Snapshot == (0, 0));
            var failed = new FakeDb { FailOpen = true }; Check((await failed.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.TechnicalFailure);
            var recovered = new FakeDb(); Check((await recovered.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.Success);
            Check(AccountSqlAdmission.Snapshot == (0, 0)); Closed(recovered);
        });
    }

}
