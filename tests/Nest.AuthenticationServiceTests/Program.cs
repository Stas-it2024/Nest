using Nest.Security;
using Nest.Security.Authentication;
using System.Reflection;
using System.Runtime.ExceptionServices;
using static Fixtures;

internal static class Program
{
    private static int assertions, scenarios;
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Authentication service invariant failed."); Interlocked.Increment(ref assertions); }
    private static async Task Scenario(string label, Func<Task> test)
    { await test(); scenarios++; Console.WriteLine("PASS scenario: " + label); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { Check(true); return; } throw new InvalidOperationException("Expected safe exception."); }
    private static async Task<int> Main()
    {
        try
        {
            await Run(); // Each concurrent scope owns bounded cleanup; never abandon Run with WaitAsync.
            Console.WriteLine($"PASS: {scenarios} service scenarios; {assertions} assertions. Real services/PasswordHasher/limiter; fake atomic storage only. No database, UI or Credential Manager.");
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine("FAIL: offline authentication services (" + error.GetType().Name + ")."); return 1; }
    }
    private static async Task Run()
    {
        await Scenario("successful login always finalizes", async () =>
        {
            var store = new FakeAccountStore();
            var result = await new AuthenticationService(store).AuthenticateAsync(Login);
            Check(result.IsAuthenticated && result.Outcome == AuthenticationOutcome.Success && result.User?.Id == 1);
            Check(store.Finds == 1 && store.Finalizes == 1 && store.CredentialRevision == 1 && store.LastPrepared == null);
        });
        await Scenario("wrong password has no finalization", async () =>
        {
            var store = new FakeAccountStore(); var crypto = new ControlledCryptography();
            var result = await new AuthenticationService(store, crypto).AuthenticateAsync(new("offline-user", Password + "!"));
            Check(result.Outcome == AuthenticationOutcome.Rejected && result.User == null);
            Check(store.Finalizes == 0 && crypto.Verifications == 1 && crypto.Preparations == 0);
        });
        var refusals = new Action<FakeAccountStore>[] {
            s => s.Missing = true, s => s.Status = StaffStatus.Inactive, s => s.DeletedAt = new DateTime(2026,1,1),
            s => s.Role = (StaffRole)99, s => { s.Format = PasswordFormat.LegacyPlaintext; s.Credential = Password; },
            s => s.Format = PasswordFormat.Disabled, s => s.Format = (PasswordFormat)99,
            s => s.Credential = "broken", s => s.Credential = null, s => s.CredentialRevision = -1,
            s => s.Role = default, s => s.Status = default, s => s.Status = (StaffStatus)99 };
        foreach (var refusal in refusals)
            await Scenario("state/format denial uses dummy and never plaintext fallback", async () =>
            {
                var store = new FakeAccountStore(); refusal(store); var crypto = new ControlledCryptography();
                var result = await new AuthenticationService(store, crypto).AuthenticateAsync(Login);
                Check(result.Outcome == AuthenticationOutcome.Rejected && !result.IsAuthenticated && result.User == null);
                Check(result.Message == "Die Anmeldung konnte nicht durchgeführt werden.");
                Check(crypto.Verifications >= 1 && crypto.Preparations == 0 && store.Finalizes == 0);
            });
        await Scenario("empty hashed legacy password can be verified without new-password policy", async () =>
        {
            var store = new FakeAccountStore { Credential = (await PreparedCredential.CreateAsync("")).Value };
            var result = await new AuthenticationService(store).AuthenticateAsync(new("offline-user", ""));
            Check(result.IsAuthenticated && store.Finalizes == 1);
        });
        await Scenario("rehash updates credential revision only", async () =>
        {
            var store = new FakeAccountStore { Credential = Old }; var crypto = new ControlledCryptography();
            var result = await new AuthenticationService(store, crypto).AuthenticateAsync(Login);
            Check(result.IsAuthenticated && crypto.Preparations == 1 && store.Finalizes == 1);
            Check(store.CredentialRevision == 2 && store.AccountRevision == 1 && !PasswordHasher.NeedsRehash(store.Credential));
            Check(await PasswordCryptography.Shared.VerifyAsync(Password, store.Credential, CancellationToken.None) == PasswordVerificationResult.Success);
        });
        await Scenario("higher work factor is never downgraded", async () =>
        {
            var store = new FakeAccountStore { Credential = Strong }; var crypto = new ControlledCryptography();
            var result = await new AuthenticationService(store, crypto).AuthenticateAsync(Login);
            Check(result.IsAuthenticated && crypto.Preparations == 0 && store.Credential == Strong && store.Finalizes == 1);
        });
        await Administration(); await Outcomes(); await AdditionalOutcomes(); await ContractEdges(); await Races(); await Cancellation(); await Limiter(); await FactoryChecks(); await HarnessChecks(); await Redaction();
    }
    private static async Task Administration()
    {
        await Scenario("create sends prepared credential only", async () =>
        {
            var store = new FakeAccountStore(); var crypto = new ControlledCryptography();
            var result = await new UserManagementService(store, crypto).CreateUserAsync(new(Profile, Password));
            Check(result.IsCommitted && store.Creates == 1 && crypto.Preparations == 1);
            Check(store.AccountRevision == AccountRules.InitialRevision && store.CredentialRevision == AccountRules.InitialRevision);
            Check(store.LastPrepared?.Format == PasswordFormat.Pbkdf2Sha256V1 && store.Credential != Password);
            Check(await PasswordCryptography.Shared.VerifyAsync(Password, store.Credential, CancellationToken.None) == PasswordVerificationResult.Success);
        });
        foreach (var invalid in new string?[] { null, "", "   ", "\t\r\n" })
            await Scenario("blank new password rejected before computation", async () =>
            {
                var store = new FakeAccountStore(); var crypto = new ControlledCryptography(); var service = new UserManagementService(store, crypto);
                Check((await service.CreateUserAsync(new(Profile, invalid))).Outcome == StoreOutcome.Rejected);
                Check((await service.ChangePasswordAsync(store.Stamp, invalid)).Outcome == StoreOutcome.Rejected);
                Check(crypto.Preparations == 0 && store.Creates == 0 && store.Passwords == 0);
            });
        await Scenario("KeepExisting profile edit performs no hash", async () =>
        {
            var store = new FakeAccountStore(); var crypto = new ControlledCryptography { Fail = true };
            var result = await new UserManagementService(store, crypto).UpdateProfileAsync(store.Stamp, Profile);
            Check(result.IsCommitted && store.Profiles == 1 && crypto.Preparations == 0 && crypto.Verifications == 0);
            Check(store.Credential == Current && store.CredentialRevision == 1 && store.AccountRevision == 2);
        });
        await Scenario("explicit Replace preserves password spaces and increments both revisions", async () =>
        {
            var store = new FakeAccountStore(); const string password = "  Synthetic replacement Ä  ";
            var result = await new UserManagementService(store).ChangePasswordAsync(store.Stamp, password);
            Check(result.IsCommitted && store.Passwords == 1 && store.Profiles == 0);
            Check(store.CredentialRevision == 2 && store.AccountRevision == 2);
            Check(await PasswordCryptography.Shared.VerifyAsync(password, store.Credential, CancellationToken.None) == PasswordVerificationResult.Success);
            Check(await PasswordCryptography.Shared.VerifyAsync(password.Trim(), store.Credential, CancellationToken.None) == PasswordVerificationResult.Failed);
        });
        await Scenario("combined edit is a single atomic command", async () =>
        {
            var store = new FakeAccountStore(); var result = await new UserManagementService(store)
                .EditUserAsync(new(store.Stamp, Profile, PasswordChange.Replace(Password)));
            Check(result.IsCommitted && store.Combined == 1 && store.Profiles == 0 && store.Passwords == 0);
            Check(store.FullName == Profile.FullName && store.Role == Profile.Role && store.CredentialRevision == 2 && store.AccountRevision == 2);
        });
        await Scenario("soft delete and stale admin stamp", async () =>
        {
            var store = new FakeAccountStore(); var service = new UserManagementService(store); var stale = store.Stamp;
            Check((await service.UpdateProfileAsync(stale, Profile)).IsCommitted);
            Check((await service.SoftDeleteAsync(stale)).Outcome == StoreOutcome.ConcurrencyConflict && store.DeletedAt == null);
            Check((await service.SoftDeleteAsync(store.Stamp)).IsCommitted && store.DeletedAt != null);
            Check(store.CredentialRevision == 1 && store.AccountRevision == 3);
        });
    }
    private static async Task Outcomes()
    {
        foreach (var outcome in new[] { StoreOutcome.Success, StoreOutcome.ConcurrencyConflict, StoreOutcome.Rejected,
            StoreOutcome.CommitUncertain, StoreOutcome.UncertainOutcome, StoreOutcome.CommittedWithCleanupFailure, StoreOutcome.TechnicalFailure })
            await Scenario("typed store outcome is preserved without retries", async () =>
            {
                var store = new FakeAccountStore { ForcedOutcome = outcome };
                var auth = await new AuthenticationService(store).AuthenticateAsync(Login);
                bool committed = outcome is StoreOutcome.Success or StoreOutcome.CommittedWithCleanupFailure;
                Check(auth.IsAuthenticated == committed && auth.FinalizationOutcome == outcome);
                Check(auth.Outcome.ToString() == outcome.ToString() && store.Finds == 1 && store.Finalizes == 1);
                Check(committed || auth.User == null);
                var admin = new UserManagementService(store);
                var written = await admin.UpdateProfileAsync(store.Stamp, Profile);
                Check(written.Outcome == outcome && written.IsCommitted == committed && store.Profiles == 1);
            });
        await Scenario("transport exceptions are sanitized and uncertain writes never retried", async () =>
        {
            var store = new FakeAccountStore { ThrowRead = true };
            Check((await new AuthenticationService(store).AuthenticateAsync(Login)).Outcome == AuthenticationOutcome.TechnicalFailure);
            Check(store.Finalizes == 0);
            store.ThrowRead = false; store.ThrowFinalize = true;
            var auth = await new AuthenticationService(store).AuthenticateAsync(Login);
            Check(!auth.IsAuthenticated && auth.Outcome == AuthenticationOutcome.UncertainOutcome && auth.FinalizationOutcome == StoreOutcome.UncertainOutcome && store.Finalizes == 1);
            store.ThrowWrite = true;
            Check((await new UserManagementService(store).UpdateProfileAsync(store.Stamp, Profile)).Outcome == StoreOutcome.UncertainOutcome);
            Check(store.Profiles == 1);
        });
    }
    private static async Task AdditionalOutcomes()
    {
        await Scenario("unknown commit can follow applied rehash without granting access or retry", async () =>
        {
            var store = new FakeAccountStore { Credential = Old, ForcedOutcome = StoreOutcome.CommitUncertain, ApplyUncertain = true };
            var result = await new AuthenticationService(store).AuthenticateAsync(Login);
            Check(result.Outcome == AuthenticationOutcome.CommitUncertain && !result.IsAuthenticated && result.User == null);
            Check(store.Finalizes == 1 && store.CredentialRevision == 2 && store.AccountRevision == 1);
            Check(await PasswordCryptography.Shared.VerifyAsync(Password, store.Credential, CancellationToken.None) == PasswordVerificationResult.Success);
            var written = await new UserManagementService(store).UpdateProfileAsync(store.Stamp, Profile);
            Check(written.Outcome == StoreOutcome.CommitUncertain && !written.IsCommitted);
            Check(store.Profiles == 1 && store.FullName == Profile.FullName && store.AccountRevision == 2);
        });
        await Scenario("confirmed rehash commit plus cleanup failure remains committed warning", async () =>
        {
            var store = new FakeAccountStore { Credential = Old, ForcedOutcome = StoreOutcome.CommittedWithCleanupFailure };
            var result = await new AuthenticationService(store).AuthenticateAsync(Login);
            Check(result.IsAuthenticated && result.Outcome == AuthenticationOutcome.CommittedWithCleanupFailure);
            Check(store.Finalizes == 1 && store.CredentialRevision == 2 && store.AccountRevision == 1);
        });
        foreach (var user in new AuthenticatedUser?[] { null, new(99,"offline-user",StaffRole.Admin,1),
            new(1,"offline-user",StaffRole.Receptionist,1), new(1,"offline-user",StaffRole.Admin,2) })
            await Scenario("invalid success payload cannot create authenticated identity", async () =>
            {
                var store = new FakeAccountStore { OmitFinalUser = user == null, ReturnedUserOverride = user };
                var result = await new AuthenticationService(store).AuthenticateAsync(Login);
                Check(result.Outcome == AuthenticationOutcome.TechnicalFailure && result.User == null && !result.IsAuthenticated);
                Check(store.Finalizes == 1);
            });
        await Scenario("invalid UTF16 and crypto exceptions cannot reach persistence", async () =>
        {
            var store = new FakeAccountStore();
            Check((await new AuthenticationService(store).AuthenticateAsync(new("offline-user", "\ud800"))).Outcome == AuthenticationOutcome.Rejected);
            Check((await new UserManagementService(store).CreateUserAsync(new(Profile, "\ud800"))).Outcome == StoreOutcome.Rejected);
            var crypto = new ControlledCryptography { Fail = true };
            Check((await new AuthenticationService(store, crypto).AuthenticateAsync(Login)).Outcome == AuthenticationOutcome.TechnicalFailure);
            Check(store.Finalizes == 0 && store.Creates == 0);
        });
    }
    private static async Task ContractEdges()
    {
        await Scenario("default enum and result values fail closed", async () =>
        {
            Check(default(StaffRole) == StaffRole.Unknown && default(StaffStatus) == StaffStatus.Unknown);
            Check(default(StoreOutcome) == StoreOutcome.Unknown && default(AuthenticationOutcome) == AuthenticationOutcome.Unknown);
            Check(default(PasswordChangeKind) == PasswordChangeKind.Unknown);
            Check((int)PasswordFormat.LegacyPlaintext == 0 && (int)PasswordFormat.Pbkdf2Sha256V1 == 1 && (int)PasswordFormat.Disabled == 2);
            Check(!new UserWriteResult().IsCommitted && !new AuthenticationResult().IsAuthenticated);
            var user = new AuthenticatedUser(1, "offline-user", StaffRole.Admin, 1);
            Check(!new AuthenticationResult(default, user, StoreOutcome.Success).IsAuthenticated);
            Check(!new AuthenticationResult(AuthenticationOutcome.Success, user, StoreOutcome.Unknown).IsAuthenticated);
            foreach (var profile in new[] { Profile with { Role = default }, Profile with { Status = default },
                Profile with { Role = (StaffRole)99 }, Profile with { Status = (StaffStatus)99 } })
            {
                var store = new FakeAccountStore(); var crypto = new ControlledCryptography();
                Check((await new UserManagementService(store, crypto).CreateUserAsync(new(profile, Password))).Outcome == StoreOutcome.Rejected);
                Check(crypto.Preparations == 0 && store.Creates == 0);
            }
        });
        foreach (var outcome in new[] { StoreOutcome.Unknown, StoreOutcome.Cancelled, (StoreOutcome)999 })
            await Scenario("invalid store outcome after admission becomes uncertain, not success or rollback", async () =>
            {
                var store = new FakeAccountStore { ForcedOutcome = outcome };
                var result = await new AuthenticationService(store).AuthenticateAsync(Login);
                Check(result.Outcome == AuthenticationOutcome.UncertainOutcome && result.FinalizationOutcome == StoreOutcome.UncertainOutcome && result.User == null);
                Check((await new UserManagementService(store).UpdateProfileAsync(store.Stamp, Profile)).Outcome == StoreOutcome.UncertainOutcome);
                Check(store.Finalizes == 1 && store.Profiles == 1);
            });
        await Scenario("null store results cannot confirm writes", async () =>
        {
            var store = new FakeAccountStore { NullFinalize = true, NullWrite = true };
            Check((await new AuthenticationService(store).AuthenticateAsync(Login)).Outcome == AuthenticationOutcome.UncertainOutcome);
            var result = await new UserManagementService(store).UpdateProfileAsync(store.Stamp, Profile);
            Check(result.Outcome == StoreOutcome.UncertainOutcome && !result.IsCommitted);
            Check(store.Finalizes == 1 && store.Profiles == 1);
        });
        foreach (var outcome in new[] { StoreOutcome.Rejected, StoreOutcome.ConcurrencyConflict, StoreOutcome.TechnicalFailure })
            await Scenario("contradictory refusal carrying a user is not a definite no-change result", async () =>
            {
                var store = new FakeAccountStore { ForcedOutcome = outcome, FailurePayload = true };
                var result = await new AuthenticationService(store).AuthenticateAsync(Login);
                Check(!result.IsAuthenticated && result.User == null && result.Outcome == AuthenticationOutcome.UncertainOutcome);
                Check(result.FinalizationOutcome == StoreOutcome.UncertainOutcome && store.Finalizes == 1);
            });
        foreach (var committed in new[] { StoreOutcome.Success, StoreOutcome.CommittedWithCleanupFailure })
            foreach (bool cancel in new[] { false, true })
                await Scenario("commit evidence survives wrong final username and cancellation", async () =>
                {
                    using var cts = new CancellationTokenSource();
                    var store = new FakeAccountStore { ForcedOutcome = committed, ReturnedUserOverride = new(1, "wrong-user", StaffRole.Admin, 1) };
                    if (cancel) store.AfterFinalize = cts.Cancel;
                    var result = await new AuthenticationService(store).AuthenticateAsync(Login, cts.Token);
                    Check(!result.IsAuthenticated && result.User == null && result.FinalizationOutcome == committed);
                    Check(result.Outcome == (cancel ? AuthenticationOutcome.Cancelled : AuthenticationOutcome.TechnicalFailure));
                    Check(store.Finalizes == 1);
                });
        await Scenario("lookup actually rejects an absent username", async () =>
        {
            var store = new FakeAccountStore(); var crypto = new ControlledCryptography();
            var result = await new AuthenticationService(store, crypto).AuthenticateAsync(new("really-absent-user", Password));
            Check(result.Outcome == AuthenticationOutcome.Rejected && result.User == null);
            Check(store.Finds == 1 && store.Finalizes == 0 && crypto.Verifications == 1);
        });
        foreach (bool credential in new[] { false, true })
            await Scenario("negative revisions and default stamps are rejected", async () =>
            {
                var store = new FakeAccountStore(); if (credential) store.CredentialRevision = -1; else store.AccountRevision = -1;
                Check((await new AuthenticationService(store).AuthenticateAsync(Login)).Outcome == AuthenticationOutcome.Rejected);
                var service = new UserManagementService(store);
                Check((await service.UpdateProfileAsync(default, Profile)).Outcome == StoreOutcome.Rejected);
                Check((await service.ChangePasswordAsync(store.Stamp, Password)).Outcome == StoreOutcome.Rejected);
                Check(store.Finalizes == 0 && store.Profiles == 0 && store.Passwords == 0);
            });
        await Scenario("max revisions permit nonincrementing reads and login", async () =>
        {
            var store = new FakeAccountStore { CredentialRevision = long.MaxValue, AccountRevision = long.MaxValue };
            var result = await new AuthenticationService(store).AuthenticateAsync(Login);
            Check(result.IsAuthenticated && result.User?.AccountRevision == long.MaxValue);
            Check(store.CredentialRevision == long.MaxValue && store.AccountRevision == long.MaxValue);
            Check(!AccountRules.TryNext(long.MaxValue, out var same) && same == long.MaxValue);
            Check(AccountRules.TryNext(long.MaxValue - 1, out var last) && last == long.MaxValue);
        });
        await Scenario("required rehash rejects exhausted credential revision before mutation", async () =>
        {
            var store = new FakeAccountStore { Credential = Old, CredentialRevision = long.MaxValue };
            var crypto = new ControlledCryptography();
            Check((await new AuthenticationService(store, crypto).AuthenticateAsync(Login)).Outcome == AuthenticationOutcome.Rejected);
            Check(crypto.Preparations == 0 && store.Finalizes == 0 && store.Credential == Old && store.CredentialRevision == long.MaxValue);
            // Direct fake-contract model also guards overflow (not proof of SQL atomicity).
            var prepared = await PasswordCryptography.Shared.PrepareAsync(Password, CancellationToken.None);
            Check((await store.FinalizeAsync(new(1, long.MaxValue, 1), prepared)).Outcome == StoreOutcome.Rejected);
            Check(store.Credential == Old && store.CredentialRevision == long.MaxValue);
        });
        await Scenario("rehash at max account revision only increments credential revision", async () =>
        {
            var store = new FakeAccountStore { Credential = Old, AccountRevision = long.MaxValue, CredentialRevision = long.MaxValue - 1 };
            Check((await new AuthenticationService(store).AuthenticateAsync(Login)).IsAuthenticated);
            Check(store.AccountRevision == long.MaxValue && store.CredentialRevision == long.MaxValue);
        });
        await Scenario("admin exhaustion has no partial profile or password changes", async () =>
        {
            var store = new FakeAccountStore { AccountRevision = long.MaxValue }; var crypto = new ControlledCryptography();
            var service = new UserManagementService(store, crypto); var stamp = store.Stamp;
            Check((await service.UpdateProfileAsync(stamp, Profile)).Outcome == StoreOutcome.Rejected);
            Check((await service.ChangePasswordAsync(stamp, Password)).Outcome == StoreOutcome.Rejected);
            Check((await service.SoftDeleteAsync(stamp)).Outcome == StoreOutcome.Rejected);
            Check(store.Profiles == 0 && store.Passwords == 0 && store.Deletes == 0 && crypto.Preparations == 0);
            var prepared = await PasswordCryptography.Shared.PrepareAsync(Password, CancellationToken.None);
            Check((await store.UpdateProfileAndCredentialAsync(stamp, Profile, prepared)).Outcome == StoreOutcome.Rejected);
            Check(store.FullName == "Synthetic User" && store.Credential == Current && store.AccountRevision == long.MaxValue && store.CredentialRevision == 1);
            store.AccountRevision = 1; store.CredentialRevision = long.MaxValue;
            Check((await service.ChangePasswordAsync(store.Stamp, Password)).Outcome == StoreOutcome.Rejected);
            Check((await service.UpdateProfileAsync(store.Stamp, Profile)).IsCommitted);
            Check(store.AccountRevision == 2 && store.CredentialRevision == long.MaxValue && store.Credential == Current);
        });
        await Scenario("combined edit conflict preserves both profile and credential", async () =>
        {
            var store = new FakeAccountStore(); var stale = store.Stamp;
            store.Mutate(t => { t.FullName = "Other admin"; t.AccountRevision = 2; });
            var result = await new UserManagementService(store).EditUserAsync(new(stale, Profile, PasswordChange.Replace(Password)));
            Check(result.Outcome == StoreOutcome.ConcurrencyConflict && !result.IsCommitted);
            Check(store.FullName == "Other admin" && store.Credential == Current && store.AccountRevision == 2 && store.CredentialRevision == 1);
            Check(store.Combined == 1 && store.Profiles == 0 && store.Passwords == 0);
        });
        await Scenario("PreparedCredential has no encoded-input wrapping constructor", async () =>
        {
            Check(typeof(PreparedCredential).GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null);
            Check(typeof(PreparedCredential).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(c => c.IsPrivate));
            var prepared = await PasswordCryptography.Shared.PrepareAsync(Old, CancellationToken.None);
            Check(prepared.Value != Old && !PasswordHasher.NeedsRehash(prepared.Value));
            Check(prepared.Value.Split('$')[4] == PasswordHasher.CurrentIterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Check(await PasswordCryptography.Shared.VerifyAsync(Old, prepared.Value, CancellationToken.None) == PasswordVerificationResult.Success);
            Check(await PasswordCryptography.Shared.VerifyAsync(Password, prepared.Value, CancellationToken.None) == PasswordVerificationResult.Failed);
            Check(await PasswordCryptography.Shared.VerifyAsync(Password, Old, CancellationToken.None) == PasswordVerificationResult.SuccessRehashNeeded);
        });
    }
    // Tracks every participant, releases every gate on failures, observes all exceptions,
    // and preserves a primary assertion instead of replacing it with a cleanup failure.
    private sealed class Participants
    {
        private readonly List<(Task Task, Type? Expected)> tasks = new();
        private readonly List<Action> releases = new(), disposals = new();
        internal T Track<T>(T task, Type? expectedError = null) where T : Task { tasks.Add((task, expectedError)); return task; }
        internal ManualResetEventSlim Gate()
        {
            var gate = new ManualResetEventSlim(); releases.Add(gate.Set); disposals.Add(gate.Dispose); return gate;
        }
        internal void ReleaseWith(Action action) => releases.Add(action);
        internal void AfterDrain(Action action) => disposals.Add(action);
        internal async Task Drain()
        {
            Exception? failure = null;
            foreach (var release in releases) try { release(); } catch (Exception ex) { failure ??= ex; }
            var all = Task.WhenAll(tasks.Select(t => t.Task));
            try { await all.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception ex)
            {
                if (!all.IsCompleted)
                {
                    // A timeout is a hard FAIL, never a successful cleanup. Observe any eventual fault too.
                    _ = all.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    failure ??= ex;
                }
            }
            foreach (var item in tasks.Where(t => t.Task.IsCompleted))
            {
                try { await item.Task; }
                catch (Exception ex) { if (item.Expected == null || !item.Expected.IsInstanceOfType(ex)) failure ??= ex; }
            }
            if (all.IsCompleted)
                foreach (var dispose in disposals) try { dispose(); } catch (Exception ex) { failure ??= ex; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
    private static async Task Concurrent(Func<Participants, Task> test)
    {
        var participants = new Participants(); Exception? primary = null, cleanup = null;
        try { await test(participants); } catch (Exception ex) { primary = ex; }
        try { await participants.Drain(); } catch (Exception ex) { cleanup = ex; }
        if (primary != null)
        {
            if (cleanup != null) primary.Data["ConcurrentCleanupErrorType"] = cleanup.GetType().Name;
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        if (cleanup != null) ExceptionDispatchInfo.Capture(cleanup).Throw();
    }
    private static async Task Races()
    {
        var mutations = new Action<FakeAccountStore>[] {
            t => { t.Credential = Strong; t.CredentialRevision = 2; t.AccountRevision = 2; },
            t => { t.Status = StaffStatus.Inactive; t.AccountRevision = 2; },
            t => { t.DeletedAt = new DateTime(2026,1,1); t.AccountRevision = 2; },
            t => { t.Role = StaffRole.Receptionist; t.AccountRevision = 2; } };
        foreach (var mutation in mutations)
            await Scenario("account mutation during verification prevents stale login", () => Concurrent(async scope =>
            {
                var store = new FakeAccountStore(); var entered = Signal(); var release = scope.Gate();
                var crypto = new ControlledCryptography { BeforeVerify = () => { entered.TrySetResult(); Block(release); } };
                var task = scope.Track(new AuthenticationService(store, crypto).AuthenticateAsync(Login));
                await Wait(entered.Task); store.Mutate(mutation); release.Set();
                var result = await task;
                Check(!result.IsAuthenticated && result.User == null);
                Check(result.Outcome is AuthenticationOutcome.ConcurrencyConflict or AuthenticationOutcome.Rejected);
                Check(store.Finds == 1 && store.Finalizes == 1);
            }));
        await Scenario("two parallel rehash attempts only one commits", () => Concurrent(async scope =>
        {
            var store = new FakeAccountStore { Credential = Old }; int arrived = 0; var ready = Signal();
            scope.ReleaseWith(() => ready.TrySetResult());
            store.BeforeFinalize = async () => { if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult(); await Wait(ready.Task); };
            var a = scope.Track(new AuthenticationService(store).AuthenticateAsync(Login));
            var b = scope.Track(new AuthenticationService(store).AuthenticateAsync(Login));
            var results = await Task.WhenAll(a, b);
            Check(results.Count(r => r.IsAuthenticated) == 1 && results.Count(r => r.Outcome == AuthenticationOutcome.ConcurrencyConflict) == 1);
            Check(store.Finalizes == 2 && store.Finds == 2 && store.CredentialRevision == 2 && store.AccountRevision == 1);
        }));
        await Scenario("admin change wins against pending rehash", () => Concurrent(async scope =>
        {
            var store = new FakeAccountStore { Credential = Old }; var entered = Signal(); var release = Signal();
            scope.ReleaseWith(() => release.TrySetResult());
            store.BeforeFinalize = async () => { entered.TrySetResult(); await Wait(release.Task); };
            var auth = scope.Track(new AuthenticationService(store).AuthenticateAsync(Login));
            await Wait(entered.Task);
            var changed = await scope.Track(new UserManagementService(store).ChangePasswordAsync(store.Stamp, "Synthetic new password"));
            Check(changed.IsCommitted); release.TrySetResult();
            Check((await auth).Outcome == AuthenticationOutcome.ConcurrencyConflict);
            Check(await PasswordCryptography.Shared.VerifyAsync("Synthetic new password", store.Credential, CancellationToken.None) == PasswordVerificationResult.Success);
            Check(store.CredentialRevision == 2 && store.AccountRevision == 2 && store.Passwords == 1 && store.Finalizes == 1);
        }));
    }
    private static async Task Cancellation()
    {
        await Scenario("cancel before computation has no effects", async () =>
        {
            using var cts = new CancellationTokenSource(); cts.Cancel(); var store = new FakeAccountStore(); var crypto = new ControlledCryptography();
            Check((await new AuthenticationService(store, crypto).AuthenticateAsync(Login, cts.Token)).Outcome == AuthenticationOutcome.Cancelled);
            Check((await new UserManagementService(store, crypto).CreateUserAsync(new(Profile, Password), cts.Token)).Outcome == StoreOutcome.Cancelled);
            Check(store.Finds == 0 && store.Finalizes == 0 && store.Creates == 0 && crypto.Preparations == 0 && crypto.Verifications == 0);
        });
        foreach (bool admin in new[] { false, true })
            await Scenario("cancel in-flight worker retains slot and skips persistence", () => Concurrent(async scope =>
            {
                using var cts = new CancellationTokenSource(); var entered = Signal(); var release = scope.Gate();
                var store = new FakeAccountStore(); Action block = () => { entered.TrySetResult(); Block(release); };
                var crypto = new ControlledCryptography { BeforeVerify = block };
                // Pause inside the real factory's admitted worker, not before factory admission.
                Action<PasswordWorkObservation> observe = item =>
                { if (admin && item.Started && item.Kind == PasswordWorkKind.Hash) block(); };
                if (admin)
                {
                    PasswordWorkLimiter.WorkObserved += observe;
                    scope.AfterDrain(() => PasswordWorkLimiter.WorkObserved -= observe);
                }
                Task task = admin ? new UserManagementService(store, crypto).CreateUserAsync(new(Profile, Password), cts.Token)
                    : new AuthenticationService(store, crypto).AuthenticateAsync(Login, cts.Token);
                _ = scope.Track(task); // Awaited/observed by the owning concurrent scope even on failure.
                await Wait(entered.Task); cts.Cancel(); Check(!task.IsCompleted); release.Set(); await task;
                Check(store.Finalizes == 0 && store.Creates == 0);
                if (admin) Check(((Task<UserWriteResult>)task).Result.Outcome == StoreOutcome.Cancelled);
                else Check(((Task<AuthenticationResult>)task).Result.Outcome == AuthenticationOutcome.Cancelled);
            }));
        foreach (var committed in new[] { StoreOutcome.Success, StoreOutcome.CommittedWithCleanupFailure, StoreOutcome.CommitUncertain })
            await Scenario("cancel after finalization preserves transaction outcome", async () =>
            {
                using var cts = new CancellationTokenSource(); var store = new FakeAccountStore { ForcedOutcome = committed, AfterFinalize = cts.Cancel };
                var result = await new AuthenticationService(store).AuthenticateAsync(Login, cts.Token);
                Check(result.Outcome == AuthenticationOutcome.Cancelled && !result.IsAuthenticated && result.User == null);
                Check(result.FinalizationOutcome == committed && store.Finalizes == 1);
            });
        await Scenario("cancel admitted admin write never claims rollback", () => Concurrent(async scope =>
        {
            using var cts = new CancellationTokenSource(); var entered = Signal(); var release = Signal(); var store = new FakeAccountStore();
            scope.ReleaseWith(() => release.TrySetResult());
            store.BeforeWrite = async () => { entered.TrySetResult(); await Wait(release.Task); };
            var task = scope.Track(new UserManagementService(store).UpdateProfileAsync(store.Stamp, Profile, cts.Token));
            await Wait(entered.Task); cts.Cancel(); Check(!task.IsCompleted); release.TrySetResult();
            Check((await task).IsCommitted && store.Profiles == 1 && store.AccountRevision == 2);
        }));
    }
    private sealed class WorkCounts
    {
        internal int Active, Peak, Started, Finished;
        internal readonly int[] Starts = new int[3], Ends = new int[3];
        internal void Observe(PasswordWorkObservation item)
        {
            if (item.Started)
            {
                Interlocked.Increment(ref Started); Interlocked.Increment(ref Starts[(int)item.Kind]);
                int current = Interlocked.Increment(ref Active), old;
                do { old = Volatile.Read(ref Peak); if (current <= old) break; }
                while (Interlocked.CompareExchange(ref Peak, current, old) != old);
            }
            else
            {
                Interlocked.Increment(ref Ends[(int)item.Kind]); Interlocked.Decrement(ref Active); Interlocked.Increment(ref Finished);
            }
        }
    }
    private static async Task Limiter()
    {
        await Scenario("actual delegate events prove common limit across verification dummy hash rehash", () => Concurrent(async scope =>
        {
            var counts = new WorkCounts(); PasswordWorkLimiter.WorkObserved += counts.Observe;
            scope.AfterDrain(() => PasswordWorkLimiter.WorkObserved -= counts.Observe);
            using var cts = new CancellationTokenSource(); var release = scope.Gate();
            int entered = 0; var both = Signal();
            string Hold()
            {
                if (Interlocked.Increment(ref entered) == 2) both.TrySetResult();
                Block(release); return PasswordHasher.HashPassword("synthetic-held-slot");
            }
            var first = scope.Track(PasswordWorkLimiter.RunAsync(Hold, cts.Token), typeof(OperationCanceledException));
            var second = scope.Track(PasswordWorkLimiter.RunAsync(Hold, CancellationToken.None));
            await Wait(both.Task); cts.Cancel();
            using var queuedCts = new CancellationTokenSource();
            var cancelled = scope.Track(PreparedCredential.CreateAsync("synthetic-cancelled-direct-factory", queuedCts.Token), typeof(OperationCanceledException));
            queuedCts.Cancel(); await ThrowsAsync<OperationCanceledException>(async () => await cancelled);
            Check(counts.Starts[(int)PasswordWorkKind.Hash] == 0 && counts.Started == 2 && counts.Active == 2);
            var directFactory = scope.Track(PreparedCredential.CreateAsync("synthetic-direct-factory"));
            var third = scope.Track(PasswordWorkLimiter.RunAsync(() => 1, CancellationToken.None));
            var storeA = new FakeAccountStore(); var storeB = new FakeAccountStore();
            var login = scope.Track(new AuthenticationService(storeA, new PasswordCryptography()).AuthenticateAsync(Login));
            var create = scope.Track(new UserManagementService(storeB, new PasswordCryptography()).CreateUserAsync(new(Profile, Password)));
            var dummyStore = new FakeAccountStore { Missing = true };
            var dummy = scope.Track(new AuthenticationService(dummyStore, new PasswordCryptography()).AuthenticateAsync(Login));
            var rehashStore = new FakeAccountStore { Credential = Old };
            var rehash = scope.Track(new AuthenticationService(rehashStore, new PasswordCryptography()).AuthenticateAsync(Login));
            var changeStore = new FakeAccountStore();
            var change = scope.Track(new UserManagementService(changeStore, new PasswordCryptography()).ChangePasswordAsync(changeStore.Stamp, Password));
            Check(counts.Started == 2 && counts.Active == 2 && counts.Peak == 2); // Real delegate events, not Task completion.
            Check(!first.IsCompleted && storeA.Finalizes == 0 && storeB.Creates == 0 && rehashStore.Finalizes == 0 && changeStore.Passwords == 0);
            release.Set(); await ThrowsAsync<OperationCanceledException>(async () => await first); await second;
            Check(await third == 1); Check((await login).IsAuthenticated && (await create).IsCommitted);
            Check((await directFactory).Format == PasswordFormat.Pbkdf2Sha256V1);
            Check(!(await dummy).IsAuthenticated && (await rehash).IsAuthenticated && (await change).IsCommitted);
            Check(counts.Active == 0 && counts.Peak == 2 && counts.Started == counts.Finished);
            Check(counts.Starts[(int)PasswordWorkKind.Verification] == 3 && counts.Ends[(int)PasswordWorkKind.Verification] == 3);
            Check(counts.Starts[(int)PasswordWorkKind.Hash] == 4 && counts.Ends[(int)PasswordWorkKind.Hash] == 4);
            var fault = scope.Track(PasswordWorkLimiter.RunAsync<int>(() => throw new InvalidOperationException("synthetic worker failure"), CancellationToken.None), typeof(InvalidOperationException));
            await ThrowsAsync<InvalidOperationException>(async () => await fault);
            Check(await scope.Track(PasswordWorkLimiter.RunAsync(() => 1, CancellationToken.None)) == 1);
            Check(counts.Active == 0 && counts.Started == counts.Finished);
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = scope.Track(completed.Task); // Scope owns completion and exception observation.
            var callerThread = new Thread(() =>
            {
                try
                {
                    int caller = Environment.CurrentManagedThreadId;
                    var worker = PasswordWorkLimiter.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None).GetAwaiter().GetResult();
                    completed.TrySetResult(worker != caller);
                }
                catch (Exception ex) { completed.TrySetException(ex); }
            }) { IsBackground = true };
            scope.AfterDrain(() => { if (!callerThread.Join(TimeSpan.FromSeconds(20))) throw new TimeoutException("Synthetic caller thread did not finish."); });
            callerThread.Start(); Check(await completed.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        }));
    }
    private static async Task FactoryChecks()
    {
        await Scenario("direct factory hashes on a background worker and owns one shared slot", () => Concurrent(async scope =>
        {
            var counts = new WorkCounts(); int caller = 0, worker = 0; PreparedCredential? prepared = null;
            Action<PasswordWorkObservation> observe = item =>
            {
                counts.Observe(item);
                if (item.Started && item.Kind == PasswordWorkKind.Hash) Interlocked.CompareExchange(ref worker, Environment.CurrentManagedThreadId, 0);
            };
            PasswordWorkLimiter.WorkObserved += observe;
            scope.AfterDrain(() => PasswordWorkLimiter.WorkObserved -= observe);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = scope.Track(completed.Task);
            var callerThread = new Thread(() =>
            {
                try
                {
                    caller = Environment.CurrentManagedThreadId;
                    prepared = PreparedCredential.CreateAsync("synthetic-direct-STA-caller").GetAwaiter().GetResult();
                    completed.TrySetResult();
                }
                catch (Exception ex) { completed.TrySetException(ex); }
            }) { IsBackground = true };
            if (OperatingSystem.IsWindows()) callerThread.SetApartmentState(ApartmentState.STA);
            scope.AfterDrain(() => { if (!callerThread.Join(TimeSpan.FromSeconds(20))) throw new TimeoutException("Synthetic factory caller did not finish."); });
            callerThread.Start(); await Wait(completed.Task);
            Check(worker != 0 && worker != caller && prepared != null);
            Check(counts.Started == 1 && counts.Finished == 1 && counts.Peak == 1 && counts.Active == 0);
            Check(counts.Starts[(int)PasswordWorkKind.Hash] == 1 && prepared!.Format == PasswordFormat.Pbkdf2Sha256V1);
            Check(!PasswordHasher.NeedsRehash(prepared!.Value));
        }));
        await Scenario("two concurrent adapter hash operations have no nested admission or deadlock", () => Concurrent(async scope =>
        {
            var counts = new WorkCounts(); var both = Signal(); var release = scope.Gate(); int entered = 0;
            Action<PasswordWorkObservation> observe = item =>
            {
                counts.Observe(item);
                if (item.Started && item.Kind == PasswordWorkKind.Hash)
                {
                    if (Interlocked.Increment(ref entered) == 2) both.TrySetResult();
                    Block(release);
                }
            };
            PasswordWorkLimiter.WorkObserved += observe;
            scope.AfterDrain(() => PasswordWorkLimiter.WorkObserved -= observe);
            var a = scope.Track(new PasswordCryptography().PrepareAsync("synthetic-parallel-factory", CancellationToken.None));
            var b = scope.Track(new PasswordCryptography().PrepareAsync("synthetic-parallel-factory", CancellationToken.None));
            await Wait(both.Task); Check(counts.Active == 2 && counts.Started == 2 && counts.Peak == 2);
            release.Set(); var results = await Task.WhenAll(a, b);
            Check(results[0].Value != results[1].Value && results[0].Value.Split('$')[5] != results[1].Value.Split('$')[5]);
            Check(counts.Finished == 2 && counts.Active == 0 && counts.Starts[(int)PasswordWorkKind.Hash] == 2);
            Check(results.All(r => !PasswordHasher.NeedsRehash(r.Value)));
        }));
        await Scenario("cancel direct factory after worker entry retains slot until real hash delegate exits", () => Concurrent(async scope =>
        {
            using var cts = new CancellationTokenSource(); var counts = new WorkCounts();
            var begin = scope.Gate(); var end = scope.Gate(); var holderGate = scope.Gate();
            var hashEntered = Signal(); var hashDerived = Signal(); var holderEntered = Signal();
            int firstWorker = 0, firstEnd = 0;
            Action<PasswordWorkObservation> observe = item =>
            {
                if (item.Started)
                {
                    counts.Observe(item);
                    if (item.Kind == PasswordWorkKind.Hash && Interlocked.CompareExchange(ref firstWorker, Environment.CurrentManagedThreadId, 0) == 0)
                    { hashEntered.TrySetResult(); Block(begin); }
                }
                else
                {
                    if (item.Kind == PasswordWorkKind.Hash && Environment.CurrentManagedThreadId == Volatile.Read(ref firstWorker) &&
                        Interlocked.CompareExchange(ref firstEnd, 1, 0) == 0)
                    { hashDerived.TrySetResult(); Block(end); } // HashPassword has returned; admitted worker has not exited yet.
                    counts.Observe(item);
                }
            };
            PasswordWorkLimiter.WorkObserved += observe;
            scope.AfterDrain(() => PasswordWorkLimiter.WorkObserved -= observe);
            var factory = scope.Track(PreparedCredential.CreateAsync("synthetic-cancelled-computation", cts.Token), typeof(OperationCanceledException));
            await Wait(hashEntered.Task);
            var holder = scope.Track(PasswordWorkLimiter.RunAsync(() => { holderEntered.TrySetResult(); Block(holderGate); return 1; }, CancellationToken.None));
            await Wait(holderEntered.Task);
            var queued = scope.Track(PreparedCredential.CreateAsync("synthetic-next-factory"));
            cts.Cancel(); Check(!factory.IsCompleted && counts.Started == 2 && counts.Active == 2);
            begin.Set(); await Wait(hashDerived.Task); // Deterministically allow real PBKDF2 to finish despite cancellation.
            Check(!factory.IsCompleted && counts.Started == 2 && counts.Starts[(int)PasswordWorkKind.Hash] == 1);
            end.Set(); await ThrowsAsync<OperationCanceledException>(async () => await factory);
            Check(factory.IsCanceled); // No PreparedCredential result can be consumed for writing.
            var prepared = await queued; Check(prepared.Format == PasswordFormat.Pbkdf2Sha256V1 && !PasswordHasher.NeedsRehash(prepared.Value));
            holderGate.Set(); Check(await holder == 1);
            Check(counts.Active == 0 && counts.Started == counts.Finished && counts.Peak == 2);
        }));
        await Scenario("factory exception restores slots and synchronous factory is absent", () => Concurrent(async scope =>
        {
            var counts = new WorkCounts(); PasswordWorkLimiter.WorkObserved += counts.Observe;
            scope.AfterDrain(() => PasswordWorkLimiter.WorkObserved -= counts.Observe);
            Check(typeof(PreparedCredential).GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) == null);
            var failed = scope.Track(PreparedCredential.CreateAsync(null!), typeof(ArgumentNullException));
            await ThrowsAsync<ArgumentNullException>(async () => await failed);
            Check(counts.Active == 0 && counts.Started == 1 && counts.Finished == 1);
            var recovered = await scope.Track(PreparedCredential.CreateAsync("synthetic-recovered-factory"));
            Check(recovered.Format == PasswordFormat.Pbkdf2Sha256V1 && !PasswordHasher.NeedsRehash(recovered.Value));
            Check(counts.Starts[(int)PasswordWorkKind.Hash] == 2 && counts.Ends[(int)PasswordWorkKind.Hash] == 2 && counts.Active == 0);
        }));
    }
    private static async Task HarnessChecks()
    {
        await Scenario("concurrent cleanup drains participants and preserves primary failure", async () =>
        {
            var primary = new InvalidOperationException("synthetic primary assertion"); Task? participant = null;
            try
            {
                await Concurrent(scope =>
                {
                    var gate = scope.Gate();
                    participant = scope.Track(Task.Run(() => { Block(gate); throw new ArgumentException("synthetic cleanup error"); }));
                    throw primary;
                });
                Check(false);
            }
            catch (InvalidOperationException ex)
            {
                Check(ReferenceEquals(ex, primary)); Check(participant?.IsCompleted == true);
                Check(ex.Data["ConcurrentCleanupErrorType"] as string == nameof(ArgumentException));
            }
        });
        await Scenario("a failed rehash participant releases its peer without indefinite wait", () => Concurrent(async scope =>
        {
            var entered = Signal(); var release = Signal(); scope.ReleaseWith(() => release.TrySetResult());
            var store = new FakeAccountStore { Credential = Old };
            store.BeforeFinalize = async () => { entered.TrySetResult(); await Wait(release.Task); };
            var peer = scope.Track(new AuthenticationService(store).AuthenticateAsync(Login));
            await Wait(entered.Task);
            var failed = scope.Track(Task.Run(() => throw new InvalidOperationException("synthetic failed peer")), typeof(InvalidOperationException));
            await ThrowsAsync<InvalidOperationException>(async () => await failed); release.TrySetResult();
            Check((await peer).IsAuthenticated);
        }));
    }
    private static async Task Redaction()
    {
        await Scenario("sensitive DTOs redacted, safe results and dependency isolation", async () =>
        {
            var secret = "synthetic-redaction-password"; var credential = Current;
            object[] sensitive = { new LoginRequest("offline-user", secret), new CreateUserRequest(Profile, secret),
                new EditUserRequest(new(1,1,1), Profile, PasswordChange.Replace(secret)), PasswordChange.Replace(secret),
                await PreparedCredential.CreateAsync(secret), new AuthenticationSnapshot(1,"offline-user",StaffRole.Admin,StaffStatus.Active,null,credential,PasswordFormat.Pbkdf2Sha256V1,1,1) };
            foreach (var value in sensitive) Check(!value.ToString()!.Contains(secret) && !value.ToString()!.Contains(credential));
            foreach (var type in new[] { typeof(AuthenticatedUser), typeof(AuthenticationResult), typeof(UserWriteResult), typeof(UserProfile) })
                Check(type.GetProperties().All(p => !p.Name.Contains("Password") && !p.Name.Contains("Credential")));
            var result = await new AuthenticationService(new FakeAccountStore { ThrowRead = true }).AuthenticateAsync(new("offline-user", secret));
            Check(!result.ToString().Contains(secret) && !result.Message.Contains(secret) && result.User == null);
            var refs = Assembly.GetExecutingAssembly().GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
            Check(!refs.Any(n => n.StartsWith("MySql") || n == "Nest" || n.Contains("PresentationFramework")));
        });
    }
}
