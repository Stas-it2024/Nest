using Nest.Data;
using System.Globalization;

internal static class B22Entry
{
    internal static bool IsRequest(string[] args) => args.Length > 0 && args[0] is "--execute-b22" or "--self-test-b22" or "--plan-b22";
    internal static bool IsWorkerRequest(string[] args) => args.Length == 7 && args[0] == "--execute-b22" && args[1] == "--worker" &&
        (args[5] == "A" && args[6] == "CheckIn" || args[5] == "B" && args[6] == "Cancel");
    internal static void RequireExecution(string[] args)
    {
        if (!args.SequenceEqual(new[] { "--execute-b22" }) && !IsWorkerRequest(args))
            throw new InvalidOperationException("Explicit B2.2 execution argument required.");
        B22DatabaseGuard.RequireAuthorization();
    }

    internal static async Task<int> Run(string[] args)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--plan-b22" }))
            {
                Console.WriteLine("PREPARED ONLY B2.2: one new room, guest and booking; CheckIn A versus Cancel B. No connections or writes.");
                Console.WriteLine("Requires --execute-b22 AND NEST_INTEGRATION_TEST=1 AND NEST_ALLOW_B22_WRITES=nest_integration_test; separate human authorization first.");
                return 0;
            }
            if (args.SequenceEqual(new[] { "--self-test-b22" })) { B22OfflineChecks.Run(); return 0; }
            RequireExecution(args);
            if (IsWorkerRequest(args))
            {
                var room = int.Parse(args[4], CultureInfo.InvariantCulture);
                B22FixturePlan.ValidateWorker(args[2], args[3], room);
                B22DatabaseGuard.ConfigureRunner();
                return await Worker(args[2], args[3], room, args[5], args[6] == "CheckIn" ? B22Operation.CheckIn : B22Operation.Cancel);
            }
            B22DatabaseGuard.ConfigureRunner();
            await ConcurrentCancellationScenario.Run();
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("FAIL B2.2: configuration, coordination, result, ownership or cleanup unconfirmed. Inspect reported marker and IDs. No automatic retry.");
            return 1;
        }
    }

    private static async Task<int> Worker(string marker, string code, int room, string id, B22Operation operation)
    {
        using (var connection = B22DatabaseGuard.Open()) B22FixtureSet.AssertWorkerOwnership(connection, marker, code, room);
        Console.WriteLine($"READY {id} {operation}");
        if (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)) != "GO") throw new InvalidOperationException("Start signal missing.");
        B22DatabaseGuard.RequireAuthorization();
        try
        {
            var repository = new ReservationRepository();
            if (operation == B22Operation.CheckIn)
            {
                var result = repository.ApplyStayTransition(code, StayTransition.CheckIn);
                Console.WriteLine("RESULT " + result);
                return WorkerRunState.ResultExitCode(result);
            }
            var cancellation = repository.CancelReservation(code);
            Console.WriteLine("RESULT " + cancellation);
            return Enum.IsDefined(cancellation) ? 0 : 2;
        }
        catch (ReservationCommitUncertainException) { Console.WriteLine("RESULT COMMIT_UNCERTAIN"); return 2; }
        catch { Console.WriteLine("RESULT TECHNICAL_ERROR"); return 2; }
    }
}
