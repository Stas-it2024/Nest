using Nest.Data;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        try
        {
            if (B22Entry.IsRequest(args)) return await B22Entry.Run(args);
            if (args.Length == 0 || args.SequenceEqual(new[] { "--plan" }))
            {
                Console.WriteLine("PREPARED ONLY: B2.1 uses new marked fixtures and two real Repository worker processes. No credentials read, connection opened or SQL executed.");
                Console.WriteLine("Real execution requires --execute-b21, NEST_INTEGRATION_TEST=1 and NEST_ALLOW_B21_WRITES=nest_integration_test. Separate human authorization is required first.");
                return 0;
            }
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                OfflineChecks.Run();
                return 0;
            }
            var isController = args.SequenceEqual(new[] { "--execute-b21" });
            var isWorker = IsWorkerRequest(args);
            if (!isController && !isWorker) throw new InvalidOperationException("Unsupported arguments.");
            TestDatabaseGuard.ConfigureRunner();
            if (isController) await ConcurrentCheckInScenario.Run();
            else return await RunWorker(args[2], args[3], int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture));
            return 0;
        }
        catch
        {
            // Never print driver exceptions, connection strings, GRANT rows or credential data.
            Console.Error.WriteLine("FAIL: B2.1 stopped. Configuration, fixture ownership, coordination, results or cleanup could not be confirmed. No automatic retry.");
            return 1;
        }
    }

    internal static bool IsWorkerRequest(string[] args) =>
        args.Length == 5 && args[0] == "--execute-b21" && args[1] == "--worker";

    private static async Task<int> RunWorker(string marker, string code, int roomId)
    {
        FixturePlan.ParseToken(marker);
        using (var validation = TestDatabaseGuard.Open())
            FixtureSet.AssertWorkerOwnership(validation, marker, code, roomId);
        Console.WriteLine("READY");
        if (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)) != "GO")
            throw new InvalidOperationException("Start signal missing.");
        TestDatabaseGuard.RequireAuthorization();
        try
        {
            // The actual public production API owns its own connection/transaction and validates it via DB.OpenConnection.
            var result = new ReservationRepository().ApplyStayTransition(code, StayTransition.CheckIn);
            Console.WriteLine("RESULT " + result);
            // All defined Repository results are reliable outcomes, including a confirmed Commit
            // followed by a cleanup failure. The controller separately enforces the B2.1 success criteria.
            return WorkerRunState.ResultExitCode(result);
        }
        catch (ReservationCommitUncertainException)
        {
            Console.WriteLine("RESULT COMMIT_UNCERTAIN");
            return 2;
        }
        catch
        {
            Console.WriteLine("RESULT TECHNICAL_ERROR");
            return 2;
        }
    }
}
