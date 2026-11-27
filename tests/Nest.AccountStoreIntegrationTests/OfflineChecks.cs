using Nest.Data;
using Nest.Security.Authentication;

internal static class OfflineChecks
{
    private static int assertions, scenarios;
    private static void Check(bool condition)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Offline assertion failed.");
    }
    private static void Reject(Action action)
    {
        var rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected);
    }
    private static Dictionary<string, string?> Allowed() => new(StringComparer.OrdinalIgnoreCase)
    {
        [IntegrationConnectionSettings.ModeVariable] = "1",
        [AccountDatabaseGuard.WriteVariable] = AccountDatabaseGuard.Database,
        [AccountDatabaseGuard.MetadataVariable] = AccountDatabaseGuard.Database
    };
    private static void Case(Action body) { scenarios++; body(); }
    private static async Task Case(Func<Task> body) { scenarios++; await body(); }
    internal static async Task<int> RunAsync()
    {
        try
        {
            Case(() =>
            {
                AccountDatabaseGuard.Authorize(new[] { AccountDatabaseGuard.ExecuteArgument }, Allowed());
                Check(true);
                foreach (var args in new[] { Array.Empty<string>(), new[] { "--self-test" },
                    new[] { "--execute-event-integrity" }, new[] { "--execute-account-store", "--self-test" },
                    new[] { "--execute-account-store", "--execute-account-store" }, new[] { "--worker" } })
                    Reject(() => AccountDatabaseGuard.Authorize(args, Allowed()));
            });
            foreach (var key in Allowed().Keys)
                foreach (var value in new string?[] { null, "", "0", "true", "hotel", "nest_integration_test ", "1 " })
                    Case(() =>
                    {
                        var env = Allowed(); env[key] = value;
                        Reject(() => AccountDatabaseGuard.Authorize(new[] { AccountDatabaseGuard.ExecuteArgument }, env));
                        env.Remove(key);
                        Reject(() => AccountDatabaseGuard.Authorize(new[] { AccountDatabaseGuard.ExecuteArgument }, env));
                    });
            Case(() =>
            {
                foreach (var value in new[] { "", "synthetic invalid input", "Database=hotel" })
                {
                    var env = Allowed(); env[IntegrationConnectionSettings.ConnectionVariable] = value;
                    Reject(() => AccountDatabaseGuard.Authorize(new[] { AccountDatabaseGuard.ExecuteArgument }, env));
                }
                foreach (var variable in new[] { "NEST_ALLOW_EVENT_INTEGRITY_WRITES", "NEST_ALLOW_ROOM_WRITES", "nest_allow_unknown_writes" })
                {
                    var env = Allowed(); env[variable] = AccountDatabaseGuard.Database;
                    Reject(() => AccountDatabaseGuard.Authorize(new[] { AccountDatabaseGuard.ExecuteArgument }, env));
                }
            });
            foreach (var host in new[] { "127.0.0.1", "remote.invalid", "localhost,remote.invalid", "localhost " })
                Case(() => Reject(() => AccountDatabaseGuard.ValidateTarget(host, 3306, AccountDatabaseGuard.Database, AccountDatabaseGuard.Runner)));
            foreach (var db in new[] { "hotel", "", "NEST_INTEGRATION_TEST", "nest_integration_test ", "other" })
                Case(() => Reject(() => AccountDatabaseGuard.ValidateTarget("localhost", 3306, db, AccountDatabaseGuard.Runner)));
            foreach (var user in new[] { "root", "nest_test_app", "", "nest_test_runner ", "other" })
                Case(() => Reject(() => AccountDatabaseGuard.ValidateTarget("localhost", 3306, AccountDatabaseGuard.Database, user)));
            foreach (var port in new uint[] { 0, 3307, 33060 })
                Case(() => Reject(() => AccountDatabaseGuard.ValidateTarget("localhost", port, AccountDatabaseGuard.Database, AccountDatabaseGuard.Runner)));
            Case(() =>
            {
                var touched = 0;
                // Valid flags still cannot cross the immutable offline boundary.
                Reject(() => AccountDatabaseGuard.Configure(new[] { AccountDatabaseGuard.ExecuteArgument }, Allowed(),
                    _ => { touched++; throw new InvalidOperationException("Synthetic missing credential."); }));
                Check(touched == 0);
                Reject(() => AccountDatabaseGuard.Configure(Array.Empty<string>(), Allowed(),
                    _ => { touched++; return "synthetic"; }));
                Check(touched == 0);
            });
            Case(() =>
            {
                const string usage = "GRANT USAGE ON *.* TO `nest_test_runner`@`localhost`";
                const string dml = "GRANT SELECT, INSERT, UPDATE, DELETE ON `nest_integration_test`.* TO `nest_test_runner`@`localhost`";
                Check(AccountDatabaseGuard.IsolatedGrants(new[] { usage, dml }));
                foreach (var grants in new[] {
                    Array.Empty<string>(), new[] { usage }, new[] { dml + " WITH GRANT OPTION" },
                    new[] { dml.Replace("nest_integration_test", "hotel", StringComparison.Ordinal) },
                    new[] { dml.Replace("SELECT, INSERT, UPDATE, DELETE", "ALL PRIVILEGES", StringComparison.Ordinal) },
                    new[] { dml, "GRANT PROCESS ON *.* TO runner" },
                    new[] { dml, "GRANT some_role TO runner" },
                    new[] { dml.Replace("SELECT, INSERT, UPDATE, DELETE", "SELECT", StringComparison.Ordinal) } })
                    Check(!AccountDatabaseGuard.IsolatedGrants(grants));
            });
            Case(() =>
            {
                var run = Guid.NewGuid(); var token = Guid.NewGuid(); var originals = new HashSet<int> { 1, 2, 3 };
                var name = "nit_" + token.ToString("N") + "_Ä";
                var marker = "NestAccountIT/" + run.ToString("N") + "/" + token.ToString("N");
                Check(AccountFixture.Owns(run, token, 4, name, marker, originals));
                Check(AccountFixture.Owns(run, token, 4, name, marker + " Кириллица Äß Ω", originals));
                Check(!AccountFixture.Owns(run, token, 3, name, marker, originals));
                Check(!AccountFixture.Owns(run, token, 0, name, marker, originals));
                Check(!AccountFixture.Owns(Guid.Empty, token, 4, name, marker, originals));
                Check(!AccountFixture.Owns(run, Guid.Empty, 4, name, marker, originals));
                Check(!AccountFixture.Owns(run, Guid.NewGuid(), 4, name, marker, originals));
                Check(!AccountFixture.Owns(Guid.NewGuid(), token, 4, name, marker, originals));
                Check(!AccountFixture.Owns(run, token, 4, name.ToUpperInvariant(), marker, originals));
                Check(!AccountFixture.Owns(run, token, 4, name, marker + "-foreign", originals));
                Check(name.Length < 50 && (marker + " Кириллица Äß Ω").Length <= 100);
                var f = new AccountFixture(run);
                Check(!f.Created && f.Id == 0 && !f.Cleaned && !f.Verified);
                Check(f.ToString().Contains(run.ToString("N"), StringComparison.Ordinal));
                Check(!f.ToString().Contains(AccountFixture.Password, StringComparison.Ordinal));
            });
            Case(() =>
            {
                for (var flags = 0; flags < 32; flags++)
                    Check(AccountFixture.MayClean((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0,
                        (flags & 8) != 0, (flags & 16) != 0) == (flags == 31));
                foreach (var outcome in Enum.GetValues<StoreOutcome>().Concat(new[] { (StoreOutcome)999 }))
                {
                    var result = new WorkerResult(outcome);
                    Check(result.Reliable == (outcome is StoreOutcome.Success or StoreOutcome.ConcurrencyConflict or StoreOutcome.Rejected));
                    Check(!new WorkerResult(outcome, false).Reliable);
                    if (!result.Reliable) Check(!AccountFixture.MayClean(true, true, result.Reliable, true, true));
                }
                // A confirmed commit with a cleanup failure is NOT treated as rollback/retryable.
                Check(AccountRules.Committed(StoreOutcome.CommittedWithCleanupFailure));
                Check(!new WorkerResult(StoreOutcome.CommittedWithCleanupFailure).Reliable);
                Check(!WorkerResult.Lost.Reliable);
            });
            await Case(async () =>
            {
                var executed = 0;
                var workers = Enumerable.Range(0, 2).Select(_ => AccountWorker.Simulate(async () =>
                { Interlocked.Increment(ref executed); await Task.Yield(); return new WorkerResult(StoreOutcome.Success); })).ToArray();
                try
                {
                    await Task.WhenAll(workers.Select(x => x.Ready)).WaitAsync(TimeSpan.FromSeconds(2));
                    Check(executed == 0);
                    foreach (var w in workers) w.Go();
                    var result = await Task.WhenAll(workers.Select(x => x.Completion));
                    Check(executed == 2 && result.All(x => x.Reliable));
                }
                finally { Check(await AccountWorker.FinishAsync(workers)); }
            });
            await Case(async () =>
            {
                var executed = false;
                var worker = AccountWorker.Simulate(() => { executed = true; return Task.FromResult(WorkerResult.Lost); });
                await worker.Ready.WaitAsync(TimeSpan.FromSeconds(2)); worker.AbortBeforeGo();
                Check(await AccountWorker.FinishAsync(new[] { worker }));
                Check(!executed && !(await worker.Completion).Reliable);
            });
            await Case(async () =>
            {
                var worker = AccountWorker.Simulate(() => throw new InvalidOperationException("Synthetic failure."));
                try
                {
                    await worker.Ready.WaitAsync(TimeSpan.FromSeconds(2)); worker.Go();
                    var result = await worker.Completion;
                    Check(!result.Reliable && result.Outcome == StoreOutcome.UncertainOutcome);
                    Check(!AccountFixture.MayClean(true, true, result.Reliable, true, true));
                }
                finally { Check(await AccountWorker.FinishAsync(new[] { worker })); }
            });
            await Case(async () =>
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var worker = AccountWorker.Simulate(async () => { await release.Task; return WorkerResult.Lost; });
                try
                {
                    await worker.Ready; worker.Go();
                    var timedOut = false;
                    try { await worker.Completion.WaitAsync(TimeSpan.FromMilliseconds(30)); }
                    catch (TimeoutException) { timedOut = true; }
                    Check(timedOut && !worker.Completion.IsCompleted);
                    Check(!AccountFixture.MayClean(true, false, true, true, true));
                }
                finally { release.TrySetResult(); Check(await AccountWorker.FinishAsync(new[] { worker })); }
                Check(!(await worker.Completion).Reliable); // Missing RESULT cannot authorize cleanup.
            });
            Case(() =>
            {
                Check(AccountScenario.Names.Length == 11);
                Check(AccountScenario.Names.Distinct(StringComparer.Ordinal).Count() == 11);
                Check(AccountDatabaseGuard.CredentialCalls == 0);
                Check(AccountDatabaseGuard.ConnectionAttempts == 0);
                Check(AccountDatabaseGuard.StoreCreations == 0);
            });
            Console.WriteLine($"PASS: {scenarios} offline scenarios; {assertions} assertions.");
            Console.WriteLine("Credential Manager calls=0; connection attempts=0; SqlAccountStore instances=0.");
            return 0;
        }
        catch
        {
            Console.WriteLine($"FAIL: offline scenario={scenarios}; assertions={assertions}. Details redacted.");
            return 1;
        }
    }
}