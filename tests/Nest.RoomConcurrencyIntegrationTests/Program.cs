using Nest.Data;
using Nest.Models;

internal static class Program
{
    internal const string ExecuteFlag = "--execute-room-concurrency";
    internal static bool IsController(string[] args) => args.SequenceEqual(new[] { ExecuteFlag });
    internal static bool IsWorker(string[] args) => args.Length == 7 && args[0] == ExecuteFlag && args[1] == "--worker";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.SequenceEqual(new[] { "--plan" }))
            {
                Console.WriteLine("PREPARED ONLY: six isolated room scenarios. No credentials read, connections opened or SQL executed.");
                Console.WriteLine("Real execution requires --execute-room-concurrency, NEST_INTEGRATION_TEST=1 and NEST_ALLOW_ROOM_CONCURRENCY_WRITES=nest_integration_test; separate human permission first.");
                return 0;
            }
            if (args.SequenceEqual(new[] { "--self-test" })) { await OfflineChecks.Run(); return 0; }
            if (!IsController(args) && !IsWorker(args)) throw new InvalidOperationException();
            RoomDatabaseGuard.ConfigureRunner();
            if (IsController(args)) await RoomScenario.RunSuite();
            else return await RunWorker(args);
            return 0;
        }
        catch
        {
            // Never print exception messages, driver diagnostics, GRANT contents or credentials.
            Console.Error.WriteLine("INCONCLUSIVE: room suite stopped. No automatic retry. Preserve any reported marker/IDs for inspection.");
            return 1;
        }
    }

    private static async Task<int> RunWorker(string[] args)
    {
        var plan = RoomPlan.Parse(args[2], args[4]);
        var id = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
        Safety.Require(id > 0 && args[6] is "A" or "B");
        var operation = RoomCase.ParseOperation(args[5]);
        using (var validation = RoomDatabaseGuard.Open())
        {
            RoomFixture.ReadOwned(validation, plan, id);
            RoomFixture.AssertNoDependencies(validation, id);
        }
        // Public production read API; the exact same SQL baseline is captured by BOTH
        // workers before either GO, and its fingerprint is verified by the controller.
        var repository = new RoomRepository();
        var expected = repository.GetById(id)?.OriginalValues ?? throw new InvalidOperationException();
        Safety.Require(RoomFixture.IsOwned(expected, plan, id) && expected.DateDelete == null);
        Console.WriteLine("READY " + Safety.Fingerprint(expected));
        if (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)) != "GO") throw new InvalidOperationException();
        try
        {
            // This preflight also rechecks credentials/grants. The Repository opens its own
            // connection via DB.OpenConnection, which validates DATABASE/CURRENT_USER/port
            // on THAT same connection before its SQL, with no USE/fallback path.
            using (var validation = RoomDatabaseGuard.Open()) RoomDatabaseGuard.AssertTarget(validation);
            RoomDatabaseGuard.RequireRunner();
            var result = operation == RoomOperation.Delete ? repository.Delete(expected)
                : repository.Update(RoomCase.Proposed(expected, plan, args[6], operation), expected);
            Console.WriteLine("RESULT " + result);
            return RoomWorkerState.ExpectedExit(result);
        }
        catch
        {
            Console.WriteLine("RESULT TECHNICAL_ERROR");
            return 2;
        }
    }
}

internal static class Safety
{
    internal static void Require(bool condition)
    { if (!condition) throw new InvalidOperationException("Room test safety/invariant could not be confirmed."); }
    internal static string Fingerprint(RoomOriginalValues values) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values)));
}
