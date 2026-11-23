using Nest.Data;

internal static class Program
{
    internal const string Execute = "--execute-event-integrity";
    internal const string Diagnose = "--diagnose-event-integrity";
    internal static bool Diagnostics(string[] args) => args.SequenceEqual(new[] { Diagnose });
    internal static bool Controller(string[] args) => args.SequenceEqual(new[] { Execute });
    internal static bool Worker(string[] args) => args.Length == 7 && args[0] == Execute && args[1] == "--worker";
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.SequenceEqual(new[] { "--plan" }))
            {
                Console.WriteLine("PREPARED ONLY: eight event scenarios. No credentials read, database connections or SQL.");
                Console.WriteLine("Real execution requires separate human permission, --execute-event-integrity, NEST_INTEGRATION_TEST=1 and NEST_ALLOW_EVENT_INTEGRITY_WRITES=nest_integration_test.");
                return 0;
            }
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                EventDatabaseGuard.Offline = true;
                OfflineChecks.Run(); return 0;
            }
            // Any diagnostic argument routes ONLY here, including invalid mixed-mode arguments.
            // This branch never configures write authorization or starts workers/fixtures.
            if (args.Contains(Diagnose, StringComparer.Ordinal)) return EventDiagnostics.Run(args);
            Safety.Require(Controller(args) || Worker(args)); // Before credentials/DB, including internal workers.
            EventDatabaseGuard.ConfigureRunner();
            if (Controller(args)) await EventScenario.RunSuite();
            else return await EventWorker.RunChild(args);
            return 0;
        }
        catch
        {
            // Never forward server errors, connection strings, grants, or raw child stderr.
            Console.Error.WriteLine("INCONCLUSIVE: event test stopped; no automatic retry. Preserve reported fixture IDs/marker for investigation.");
            return 1;
        }
    }
}

internal static class Safety
{
    internal static void Require(bool condition)
    { if (!condition) throw new InvalidOperationException("Event test safety/invariant not confirmed."); }
    internal static string Fingerprint<T>(T value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value)));
    internal static string RepositoryBuild => typeof(EventRepository).Module.ModuleVersionId.ToString("N");
}
