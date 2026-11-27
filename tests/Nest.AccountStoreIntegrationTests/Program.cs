using System.Collections;
using Nest.Data;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        // Offline selection precedes every credential/configuration/network action.
        if (args.SequenceEqual(new[] { "--self-test" }))
        {
            AccountDatabaseGuard.SealOffline();
            return await OfflineChecks.RunAsync();
        }
        if (args.Length == 0 || args.SequenceEqual(new[] { "--plan" }))
        {
            Console.WriteLine("PLAN ONLY: no credentials, connections or fixtures.");
            foreach (var name in AccountScenario.Names) Console.WriteLine(name);
            return 0;
        }
        try
        {
            var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .ToDictionary(x => (string)x.Key, x => x.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
            AccountDatabaseGuard.Authorize(args, environment); // Before Credential Manager, DB or Store.
            using var guard = AccountDatabaseGuard.Configure(args, environment, WindowsCredentialReader.ReadPassword);
            return await AccountScenario.RunAsync(guard);
        }
        catch (Exception error)
        {
            // Never print exception.Message, driver text, requests, credentials or connection strings.
            AccountDatabaseGuard.ReportFailure(error);
            Console.WriteLine("NOT VERIFIED: admission/preflight/run failed. No automatic retry. Exit 2.");
            return 2;
        }
    }
}