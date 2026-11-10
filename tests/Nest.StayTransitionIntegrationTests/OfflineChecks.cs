using Nest.Data;
using System.Globalization;
using System.Reflection;

internal static class OfflineChecks
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool value) { if (!value) throw new InvalidOperationException("Offline invariant failed."); checks++; }
        Check(Program.IsWorkerRequest(new[] { "--execute-b21", "--worker", "marker", "code", "123" }));
        Check(!Program.IsWorkerRequest(new[] { "--worker", "marker", "code", "123" }));
        Check(!Program.IsWorkerRequest(new[] { "--plan", "--worker", "marker", "code", "123" }));
        Check(!Program.IsWorkerRequest(new[] { "--execute-b21", "--worker", "marker", "code" }));
        for (var i = 0; i < 100; i++)
        {
            var plan = FixturePlan.Create();
            Check(FixturePlan.ParseToken(plan.Marker) == plan.Token && plan.Marker.Length <= 50);
            Check(plan.CodeA.Length == 20 && plan.CodeB.Length == 20 && plan.CodeA != plan.CodeB);
            Check(plan.Email("A").Length <= 100 && plan.Email("A") != plan.Email("B"));
            Check(plan.RoomNumber.ToString(CultureInfo.InvariantCulture).Length <= 10 && plan.RoomNumber > 0);
            Check(plan.Today == DateTime.Today && plan.Today.AddDays(1) < plan.Today.AddDays(2));
        }
        foreach (var first in Enum.GetValues<StayTransitionResult>())
        foreach (var second in Enum.GetValues<StayTransitionResult>())
            Check(ConcurrentCheckInScenario.ResultsAreCorrect(first, second) ==
                  (new[] { first, second }.Count(result => result == StayTransitionResult.Success) == 1 && new[] { first, second }.Count(result => result == StayTransitionResult.OccupancyConflict) == 1));
        Check(ConcurrentCheckInScenario.IsRoomLockStatement("SELECT id, status, geloescht_am FROM zimmer WHERE id = 123 FOR UPDATE;", 123));
        Check(!ConcurrentCheckInScenario.IsRoomLockStatement("SELECT id, status, geloescht_am FROM zimmer WHERE id = 1234 FOR UPDATE;", 123));
        Check(!ConcurrentCheckInScenario.IsRoomLockStatement("SELECT id, status, geloescht_am FROM zimmer WHERE id = 123;", 123));
        Check(!ConcurrentCheckInScenario.IsRoomLockStatement("SELECT id FROM reservierungen WHERE id = 123 FOR UPDATE;", 123));
        var usage = "GRANT USAGE ON *.* TO `nest_test_runner`@`localhost`";
        var scoped = "GRANT SELECT, INSERT, UPDATE, DELETE ON `nest_integration_test`.* TO `nest_test_runner`@`localhost`";
        Check(TestDatabaseGuard.GrantsAreIsolated(new[] { usage, scoped }));
        Check(!TestDatabaseGuard.GrantsAreIsolated(new[] { usage }));
        Check(!TestDatabaseGuard.GrantsAreIsolated(new[] { scoped, "GRANT SELECT ON `hotel`.* TO `nest_test_runner`@`localhost`" }));
        Check(!TestDatabaseGuard.GrantsAreIsolated(new[] { scoped + " WITH GRANT OPTION" }));
        Check(!TestDatabaseGuard.GrantsAreIsolated(new[] { scoped, "GRANT PROCESS ON *.* TO `nest_test_runner`@`localhost`" }));
        foreach (var field in typeof(FixtureSet).GetFields(BindingFlags.Static | BindingFlags.NonPublic).Where(field => field.IsLiteral && field.FieldType == typeof(string)))
        {
            var sql = (string)(field.GetRawConstantValue() ?? throw new InvalidOperationException());
            Check(sql.StartsWith("INSERT INTO ", StringComparison.Ordinal) || sql.StartsWith("DELETE FROM ", StringComparison.Ordinal));
            Check(!sql.Contains("hotel.", StringComparison.OrdinalIgnoreCase) && !sql.Contains("UPDATE zimmer", StringComparison.OrdinalIgnoreCase));
            if (sql.StartsWith("DELETE", StringComparison.Ordinal)) Check(sql.Contains("WHERE id = @id") && sql.Contains("@marker"));
        }
        var mode = Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable);
        var write = Environment.GetEnvironmentVariable(TestDatabaseGuard.WriteVariable);
        try
        {
            foreach (var value in new string?[] { null, "", "0", "true", "wrong" })
            {
                Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, value);
                Environment.SetEnvironmentVariable(TestDatabaseGuard.WriteVariable, IntegrationConnectionSettings.DatabaseName);
                try { TestDatabaseGuard.RequireAuthorization(); throw new Exception("Unsafe authorization"); }
                catch (InvalidOperationException) { Check(true); }
            }
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, "1");
            foreach (var value in new string?[] { null, "", "hotel", "1", "true", "nest_integration_test " })
            {
                Environment.SetEnvironmentVariable(TestDatabaseGuard.WriteVariable, value);
                try { TestDatabaseGuard.RequireAuthorization(); throw new Exception("Unsafe authorization"); }
                catch (InvalidOperationException) { Check(true); }
            }
            Environment.SetEnvironmentVariable(TestDatabaseGuard.WriteVariable, IntegrationConnectionSettings.DatabaseName);
            TestDatabaseGuard.RequireAuthorization(); Check(true); // Pure validation only, no ConfigureRunner/Open.
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, mode);
            Environment.SetEnvironmentVariable(TestDatabaseGuard.WriteVariable, write);
        }
        var safetyChecks = WorkerSafetyChecks.Run();
        Console.WriteLine($"PASS: {checks} offline plan/security checks + {safetyChecks} worker/cleanup safety checks; no credential retrieval, connections, fixture writes or Repository operations.");
    }
}
