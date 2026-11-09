using MySql.Data.MySqlClient;
using Nest.Data;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class ConfigurationTests
{
    [DllImport("kernel32.dll", EntryPoint = "SetEnvironmentVariableW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetNativeEnvironmentVariable(string name, string value);

    public static void Run()
    {
        var count = 0;
        void Check(bool condition)
        {
            if (!condition) throw new InvalidOperationException();
            count++;
        }
        void Set(string? mode, string? value)
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, mode);
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, value);
        }
        void Reject(string? mode, string? value)
        {
            Set(mode, value);
            try { IntegrationConnectionSettings.FromEnvironment(); }
            catch (InvalidOperationException) { Check(true); return; }
            throw new InvalidOperationException();
        }
        var valid = new MySqlConnectionStringBuilder
        {
            Server = "localhost", Port = 3306, Database = IntegrationConnectionSettings.DatabaseName,
            UserID = "nest_test_runner", Password = "non-secret-self-test-value", UseAffectedRows = false
        }.ConnectionString;
        Set(null, null);
        Check(IntegrationConnectionSettings.FromEnvironment() == null);
        try { IntegrationConnectionSettings.FromEnvironment(requireTestMode: true); throw new Exception(); }
        catch (InvalidOperationException) { Check(true); }
        Reject(null, valid);
        Reject("0", valid);
        Reject("true", valid);
        Reject("invalid", valid);
        Reject(" 1 ", valid);
        foreach (var value in new string?[] { null, valid })
        {
            Set(null, value);
            Check(SetNativeEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, ""));
            Check(Environment.GetEnvironmentVariables().Contains(IntegrationConnectionSettings.ModeVariable));
            try { IntegrationConnectionSettings.FromEnvironment(); throw new Exception(); }
            catch (InvalidOperationException) { Check(true); }
        }
        Reject("1", null);
        Reject("1", "");
        Reject("1", "not a connection string");
        foreach (var change in new (string Key, object Value)[]
        {
            ("Server", "127.0.0.1"), ("Server", "remote"), ("Server", "localhost,remote"),
            ("Port", 3307), ("Database", "hotel"), ("Database", "other"),
            ("User ID", "root"), ("User ID", "nest_test_schema"), ("User ID", "other"),
            ("Password", ""), ("UseAffectedRows", true), ("Pooling", true), ("ConnectionProtocol", "Pipe")
        })
        {
            var altered = new MySqlConnectionStringBuilder(valid) { [change.Key] = change.Value };
            Reject("1", altered.ConnectionString);
        }
        foreach (var key in new[] { "Server", "Port", "Database", "User ID", "Password", "UseAffectedRows" })
        {
            var incomplete = new MySqlConnectionStringBuilder(valid);
            incomplete.Remove(key);
            Reject("1", incomplete.ConnectionString);
        }
        foreach (var user in new[] { "nest_test_app", "nest_test_runner" })
        {
            var builder = new MySqlConnectionStringBuilder(valid) { UserID = user };
            Set("1", builder.ConnectionString);
            var settings = IntegrationConnectionSettings.FromEnvironment(requireTestMode: true)
                ?? throw new InvalidOperationException();
            Check(settings.UserName == user);
            var parsed = new MySqlConnectionStringBuilder(settings.ConnectionString);
            Check(!parsed.UseAffectedRows && !parsed.Pooling && !parsed.PersistSecurityInfo);
            Check(parsed.Database == IntegrationConnectionSettings.DatabaseName && parsed.Server == "localhost" && parsed.Port == 3306);
        }
        foreach (var scenario in new[] { "normal-to-test", "test-to-normal", "test-user-change", "tampered", "foreign", "valid" })
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--configuration-lifetime-test");
            start.ArgumentList.Add(scenario);
            using var child = Process.Start(start) ?? throw new InvalidOperationException();
            child.StandardOutput.ReadToEnd();
            child.StandardError.ReadToEnd();
            child.WaitForExit();
            Check(child.ExitCode == 0);
        }
        Console.WriteLine($"PASS: {count} isolierte Konfigurationsprüfungen einschließlich leerem Windows-Wert und 6 Prozesslebensdauer-Szenarien; keine Verbindung geöffnet.");
    }

    public static void RunLifetimeTest(string scenario)
    {
        var valid = new MySqlConnectionStringBuilder
        {
            Server = "localhost", Port = 3306, Database = IntegrationConnectionSettings.DatabaseName,
            UserID = "nest_test_runner", Password = "non-secret-self-test-value", UseAffectedRows = false
        }.ConnectionString;
        var normal = scenario == "normal-to-test";
        Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, normal ? null : "1");
        Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, normal ? null : valid);
        using var connection = DB.GetConnection();
        if (connection.State != ConnectionState.Closed) throw new InvalidOperationException();
        var disposed = false;
        connection.Disposed += (_, _) => disposed = true;

        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new Exception();
        }
        if (scenario == "valid")
        {
            using var second = DB.GetConnection();
            if (ReferenceEquals(connection, second) || second.State != ConnectionState.Closed)
                throw new InvalidOperationException();
            return;
        }
        if (scenario == "foreign")
        {
            using var foreign = new MySqlConnection(valid);
            Reject(() => DB.OpenConnection(foreign));
            if (foreign.State != ConnectionState.Closed) throw new InvalidOperationException();
            return;
        }
        if (scenario == "tampered")
            connection.ConnectionString = new MySqlConnectionStringBuilder(valid) { Database = "hotel" }.ConnectionString;
        else
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, normal ? "1" : scenario == "test-user-change" ? "1" : null);
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable,
                normal ? valid : scenario == "test-user-change" ? new MySqlConnectionStringBuilder(valid) { UserID = "nest_test_app" }.ConnectionString : null);
            Reject(() => { using var unexpected = DB.GetConnection(); });
        }
        Reject(() => DB.OpenConnection(connection));
        if (!disposed || connection.State != ConnectionState.Closed) throw new InvalidOperationException();
    }
}
