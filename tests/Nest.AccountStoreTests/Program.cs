using Nest.Security.Authentication;

namespace AccountStoreTests;
internal static class Program
{
    internal static int Assertions, Scenarios;
    internal static PreparedCredential Credential = null!;
    internal static readonly UserProfile Profile = new("Synthetic", "Offline Fixture", StaffRole.Admin, StaffStatus.Active);
    internal static readonly AccountStamp Stamp = new(7, 4, 9);
    internal static readonly AuthenticationStamp AuthStamp = new(7, 4, 9);
    internal static void Check(bool value, string label = "invariant")
    { Assertions++; if (!value) throw new InvalidOperationException("Assertion failed: " + label); }
    internal static async Task Scenario(string name, Func<Task> action)
    { try { await action(); Scenarios++; } catch (Exception error) { throw new InvalidOperationException("Scenario failed: " + name + "; " + (error.Message.StartsWith("Assertion failed:", StringComparison.Ordinal) ? error.Message : error.GetType().Name)); } }
    internal static async Task<Exception> Error(Func<Task> action)
    { try { await action(); } catch (Exception ex) { return ex; } throw new InvalidOperationException("Expected failure did not occur."); }
    internal static void Closed(FakeDb db)
    {
        var operations = db.Trace.Count(x => x == "open");
        Check(db.CloseAttempts == operations && db.DisposeAttempts == operations, "both cleanup paths attempted once per operation");
        Check(db.CloseSuccesses == (db.FailClose ? 0 : operations) && db.DisposeSuccesses == (db.FailConnectionDispose ? 0 : operations), "cleanup success is not an attempt");
        Check(db.ConnectionReleased == (!db.FailClose || !db.FailConnectionDispose), "fake physical release accurately reported");
        if (db.ConnectionReleased) Check(db.InTransaction == 0 && !db.PendingIsolation);
        Check(db.ActiveTransactions == (db.FailTransactionDispose ? 1 : 0), "wrapper disposal failure remains observable");
        Check(db.Calls.All(c => c.Timeout == 10), "bounded command timeout");
        Check(!db.Calls.Any(c => c.Sql.Contains("SELECT *", StringComparison.OrdinalIgnoreCase) || c.Sql.Contains("SET GLOBAL", StringComparison.OrdinalIgnoreCase) || c.Sql.Contains("ALTER ", StringComparison.OrdinalIgnoreCase)), "no unapproved SQL");
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0) return SqlAccountStoreChecks.DbChild(args);
            await SqlAccountStoreChecks.DbConfigurationChecks();
            Credential = await PreparedCredential.CreateAsync("synthetic-only-password");
            await SqlAccountStoreChecks.Run();
            await TransactionChecks.Run();
            Check(Nest.Data.Authentication.AccountSqlAdmission.Snapshot == (0, 0), "all SQL participants drained");
            Console.WriteLine($"PASS: {Scenarios} scenarios; {Assertions} assertions. Production Store/session/schema guard; fake ADO.NET only. No MariaDB connections, WPF or Credential Manager.");
            return 0;
        }
        catch (Exception ex)
        {
            // Test labels are fixed; never print SQL, snapshots, credentials or transport exceptions.
            Console.Error.WriteLine(ex.GetType().Name + ": " + (ex.Message.StartsWith("Scenario failed:", StringComparison.Ordinal) ? ex.Message : "Offline test failed."));
            return 1;
        }
    }
}
