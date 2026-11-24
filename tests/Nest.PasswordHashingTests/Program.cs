using Nest.Security;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

internal static class Program
{
    private static int checks;
    private const string Synthetic = "Синтетический пароль Äß Ω";
    private const string Salt = "AAECAwQFBgcICQoLDA0ODw==";
    // Independently generated with Python hashlib.pbkdf2_hmac('sha256', UTF8(password), bytes(range(16)), n, 32).
    // Synthetic public test fixtures, never account credentials.
    private static readonly (int Iterations, string Hash)[] Vectors = {
        (100000, "NBDXUqEGGnZ6a5ImW6afSFoYWiTrTz63X09R1MxNgO0="),
        (600000, "RKFiwVeyMhcIzDwIo+faCKUyEM8cRVu7JZEGBVPlEa8="),
        (1200000, "EZzBMuqdymsJ9rzieLjHuQRTNhNa458uspRbqzfa5RA=") };
    private static string Credential(int iterations, string? salt = null, string? hash = null) =>
        "$nest$pbkdf2-sha256$v1$" + iterations.ToString(CultureInfo.InvariantCulture) + "$" + (salt ?? Salt) + "$" + (hash ?? Vectors[1].Hash);
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException("Hashing invariant failed: " + name); checks++; }
    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { checks++; return; }
        throw new InvalidOperationException("Expected exception: " + name);
    }
    private static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--benchmark")) return 2;
        try
        {
            Functional(); InvalidCredentials(); IndependentVectors();
            ParallelCalls(); CredentialLengthBoundary(); RehashBoundary();
            Console.WriteLine($"PASS: {checks} password-hashing offline assertions; 6 scenario groups (3 existing + 3 added); no database, UI or credentials.");
            if (args.Length == 1) Benchmark();
            return 0;
        }
        catch (Exception ex)
        {
            // No password, credential, request or exception Message/StackTrace in diagnostics.
            Console.Error.WriteLine("FAIL: password-hashing checks (" + ex.GetType().Name + ")."); return 1;
        }
    }
    private static void Functional()
    {
        var encoded = PasswordHasher.HashPassword(Synthetic);
        Check(encoded.Length <= 255 && encoded.Length == 99, "VARCHAR fit and length");
        var fields = encoded.Split('$');
        Check(fields.Length == 7 && fields[2] == "pbkdf2-sha256" && fields[3] == "v1" && fields[4] == "600000", "format");
        Check(Convert.FromBase64String(fields[5]).Length == 16 && Convert.FromBase64String(fields[6]).Length == 32, "sizes");
        Check(PasswordHasher.VerifyPassword(Synthetic, encoded) == PasswordVerificationResult.Success, "correct password");
        Check(PasswordHasher.VerifyPassword(Synthetic + "x", encoded) == PasswordVerificationResult.Failed, "wrong password");
        Check(!PasswordHasher.NeedsRehash(encoded), "current parameters");
        foreach (var text in new[] { "CaseSensitive", " leading", "trailing ", " both ", "Пароль кириллица", "Grüße ÄÖÜäöüß", "漢字🔐Ω", "", "\0embedded\0", new string('x', 65536) + "я" })
        {
            var hash = PasswordHasher.HashPassword(text);
            Check(PasswordHasher.VerifyPassword(text, hash) == PasswordVerificationResult.Success, "exact input preserved");
            Check(PasswordHasher.VerifyPassword(text + "!", hash) == PasswordVerificationResult.Failed, "suffix not truncated");
        }
        var mixed = PasswordHasher.HashPassword("CaseSensitive");
        Check(PasswordHasher.VerifyPassword("casesensitive", mixed) == PasswordVerificationResult.Failed, "case");
        var spaces = PasswordHasher.HashPassword(" abc ");
        foreach (var text in new[] { "abc", " abc", "abc " })
            Check(PasswordHasher.VerifyPassword(text, spaces) == PasswordVerificationResult.Failed, "no trimming");
        var composed = PasswordHasher.HashPassword("é");
        Check(PasswordHasher.VerifyPassword("e\u0301", composed) == PasswordVerificationResult.Failed, "no normalization");
        var longPassword = new string('x', 65536) + "end";
        var longHash = PasswordHasher.HashPassword(longPassword);
        Check(PasswordHasher.VerifyPassword(new string('x', 65536) + "END", longHash) == PasswordVerificationResult.Failed, "long suffix significant");
        var salts = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 4; i++) Check(salts.Add(PasswordHasher.HashPassword(Synthetic).Split('$')[5]), "individual random salt");
        // Future migration may hash any legacy value, even one looking like a credential.
        var formatLookingPassword = Credential(600000);
        var migrated = PasswordHasher.HashPassword(formatLookingPassword);
        Check(PasswordHasher.VerifyPassword(formatLookingPassword, migrated) == PasswordVerificationResult.Success, "legacy format collision can migrate explicitly");
        Check(PasswordHasher.VerifyPassword(Synthetic, Synthetic) == PasswordVerificationResult.InvalidCredential, "no plaintext fallback");
        Throws<ArgumentNullException>(() => PasswordHasher.HashPassword(null!), "null create");
        Throws<ArgumentNullException>(() => PasswordHasher.VerifyPassword(null!, encoded), "null verify");
        Throws<EncoderFallbackException>(() => PasswordHasher.HashPassword("\ud800"), "invalid UTF16 create");
        Throws<EncoderFallbackException>(() => PasswordHasher.VerifyPassword("\ud800", encoded), "invalid UTF16 verify");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var invariant = PasswordHasher.HashPassword("synthetic");
            Check(invariant.Split('$')[4] == "600000" && !PasswordHasher.NeedsRehash(invariant), "culture independent");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }
    private static void InvalidCredentials()
    {
        var valid = Credential(600000);
        var invalid = new List<string?> { null, "", "plain-password", valid + "$extra", valid[..valid.LastIndexOf('$')],
            valid.Replace("pbkdf2-sha256", "pbkdf2-sha1"), valid.Replace("v1", "v2"), valid.Replace("v1", "V1"),
            valid.Replace("nest", "NEST"), valid.TrimStart('$'), " " + valid, valid + " ", new string('x', 256),
            valid.Replace(Salt, Salt.Replace("w==", "x==")), valid.Replace("Ea8=", "Ea9="),
            valid.Replace(Salt, "!" + Salt[1..]), valid.Replace(Salt, Salt.Replace("=", " ")),
            valid.Replace(Vectors[1].Hash, "!" + Vectors[1].Hash[1..]), valid.Replace(Salt, Salt[..^1]),
            valid.Replace(Salt, " " + Salt[1..]) };
        foreach (var n in new[] { "-1", "0", "1", "99999", "1200001", "2147483647", "2147483648", "9999999999999999999", "0600000", "+600000", "600000 ", " 600000", "6e5", "٦٠٠٠٠٠", "600000\0" })
            invalid.Add(valid.Replace("600000", n));
        foreach (var size in new[] { 0, 1, 15, 17, 18, 32 }) invalid.Add(Credential(600000, Convert.ToBase64String(new byte[size])));
        foreach (var size in new[] { 0, 1, 16, 31, 33, 34, 64 }) invalid.Add(Credential(600000, hash: Convert.ToBase64String(new byte[size])));
        foreach (var bad in invalid)
        {
            // Malformed UTF16 proves invalid metadata is rejected before password encoding/KDF.
            Check(PasswordHasher.VerifyPassword("\ud800", bad) == PasswordVerificationResult.InvalidCredential, "reject before KDF");
            Throws<FormatException>(() => PasswordHasher.NeedsRehash(bad), "invalid metadata not rehashable");
        }
        for (int length = 256; length < 456; length++)
            Check(PasswordHasher.VerifyPassword(Synthetic, new string('$', length)) == PasswordVerificationResult.InvalidCredential, "bounded parser");
        var changed = Convert.FromBase64String(Vectors[1].Hash); changed[0] ^= 1;
        Check(PasswordHasher.VerifyPassword(Synthetic, Credential(600000, hash: Convert.ToBase64String(changed))) == PasswordVerificationResult.Failed, "valid but altered hash");
        Check(PasswordHasher.VerifyPassword(Synthetic, Credential(600000, Convert.ToBase64String(new byte[16]))) == PasswordVerificationResult.Failed, "valid but altered salt");
    }
    private static void IndependentVectors()
    {
        foreach (var vector in Vectors)
        {
            var encoded = Credential(vector.Iterations, hash: vector.Hash);
            Check(PasswordHasher.VerifyPassword(Synthetic, encoded) == (vector.Iterations < 600000
                ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success), "independent Python vector through production verifier");
            Check(PasswordHasher.NeedsRehash(encoded) == (vector.Iterations < 600000), "rehash without downgrade");
            Check(PasswordHasher.VerifyPassword(Synthetic + "!", encoded) == PasswordVerificationResult.Failed, "wrong password never rehash success");
        }
        // RFC 7914 section 11 primitive vectors (short salts/work factors deliberately invalid in the credential codec).
        // https://www.rfc-editor.org/rfc/rfc7914.html#section-11
        foreach (var vector in new[] {
            ("passwd", "salt", 1, "55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc49ca9cccf179b645991664b39d77ef317c71b845b1e30bd509112041d3a19783"),
            ("Password", "NaCl", 80000, "4ddcd8f60b98be21830cee5ef22701f9641a4418d04c0414aeff08876b34ab56a1d425a1225833549adb841b51c9b3176a272bdebba1d078478f62b397f33c8d") })
        {
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(vector.Item1), Encoding.ASCII.GetBytes(vector.Item2), vector.Item3, HashAlgorithmName.SHA256, 64);
            Check(Convert.ToHexString(actual).Equals(vector.Item4, StringComparison.OrdinalIgnoreCase), "RFC7914 PBKDF2 SHA256");
            CryptographicOperations.ZeroMemory(actual);
        }
    }
    private static void ParallelCalls()
    {
        const int workers = 2, rounds = 3;
        using var gate = new Barrier(workers);
        int active = 0, peak = 0;
        var samples = new (string Credential, PasswordVerificationResult Correct,
            PasswordVerificationResult Wrong, PasswordVerificationResult OtherWorker)[workers, rounds];
        T Together<T>(Func<T> operation)
        {
            var current = Interlocked.Increment(ref active);
            int previous;
            do
            {
                previous = Volatile.Read(ref peak);
                if (current <= previous) break;
            } while (Interlocked.CompareExchange(ref peak, current, previous) != previous);
            try
            {
                // Both dedicated workers enter each phase before calling the component.
                // No more than two expensive operations can be in flight.
                if (!gate.SignalAndWait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Parallel password test coordination timed out.");
                return operation();
            }
            finally { Interlocked.Decrement(ref active); }
        }
        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(() =>
        {
            for (int round = 0; round < rounds; round++)
            {
                var password = "synthetic-parallel-" + worker + "-" + round + " ÄΩ";
                var otherPassword = "synthetic-parallel-" + (1 - worker) + "-" + round + " ÄΩ";
                var credential = Together(() => PasswordHasher.HashPassword(password));
                var correct = Together(() => PasswordHasher.VerifyPassword(password, credential));
                var wrong = Together(() => PasswordHasher.VerifyPassword(password + "!", credential));
                var other = Together(() => PasswordHasher.VerifyPassword(otherPassword, credential));
                samples[worker, round] = (credential, correct, wrong, other);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task.WaitAll(tasks); // Any worker exception fails the suite; never convert it to PASS.
        Check(tasks.All(task => task.IsCompletedSuccessfully), "parallel workers completed without exception");
        Check(peak == workers && active == 0, "two concurrent callers, no operation left in flight");
        var salts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            Check(sample.Correct == PasswordVerificationResult.Success, "parallel correct password");
            Check(sample.Wrong == PasswordVerificationResult.Failed, "parallel incorrect password");
            Check(sample.OtherWorker == PasswordVerificationResult.Failed, "parallel worker inputs not mixed");
            Check(salts.Add(sample.Credential.Split('$')[5]), "parallel individual random salt");
        }
    }
    private static void CredentialLengthBoundary()
    {
        foreach (int length in new[] { 255, 256 })
        {
            // Valid-looking prefix, but an overlong hash field: 255 is not an admission bypass.
            var credential = Credential(600000).PadRight(length, 'A');
            Check(credential.Length == length, "exact credential length boundary");
            Check(PasswordHasher.VerifyPassword(Synthetic, credential) == PasswordVerificationResult.InvalidCredential,
                "length boundary is invalid metadata, not a password mismatch");
            Check(PasswordHasher.VerifyPassword("\ud800", credential) == PasswordVerificationResult.InvalidCredential,
                "invalid structure rejected before password encoding at length boundary");
            Throws<FormatException>(() => PasswordHasher.NeedsRehash(credential), "length boundary not rehashable");
        }
    }
    private static void RehashBoundary()
    {
        foreach (int iterations in new[] { 599999, 600000, 600001 })
        {
            // Construct synthetic boundary fixtures with the BCL directly, independently of HashPassword.
            var passwordBytes = Encoding.UTF8.GetBytes(Synthetic);
            var salt = Convert.FromBase64String(Salt);
            byte[]? hash = null;
            try
            {
                hash = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, 32);
                var credential = Credential(iterations, hash: Convert.ToBase64String(hash));
                var expected = iterations < 600000 ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success;
                Check(PasswordHasher.VerifyPassword(Synthetic, credential) == expected, "exact rehash verification boundary");
                Check(PasswordHasher.NeedsRehash(credential) == (iterations < 600000), "exact rehash metadata boundary, no downgrade");
                Check(PasswordHasher.VerifyPassword(Synthetic + "!", credential) == PasswordVerificationResult.Failed,
                    "wrong boundary password never authorizes rehash");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                CryptographicOperations.ZeroMemory(salt);
                if (hash != null) CryptographicOperations.ZeroMemory(hash);
            }
        }
    }
    private static void Benchmark()
    {
        const int warmup = 2, samples = 10;
        const string password = "synthetic-benchmark-only ÄΩ";
        var credential = PasswordHasher.HashPassword(password);
        for (int i = 0; i < warmup; i++) { _ = PasswordHasher.HashPassword(password); _ = PasswordHasher.VerifyPassword(password, credential); }
        var create = new List<double>(); var verify = new List<double>();
        for (int i = 0; i < samples; i++)
        {
            var timer = Stopwatch.StartNew(); _ = PasswordHasher.HashPassword(password); timer.Stop(); create.Add(timer.Elapsed.TotalMilliseconds);
            timer.Restart(); var result = PasswordHasher.VerifyPassword(password, credential); timer.Stop(); verify.Add(timer.Elapsed.TotalMilliseconds);
            if (result != PasswordVerificationResult.Success) throw new InvalidOperationException("Benchmark verification failed.");
        }
        Console.WriteLine($"BENCH environment: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; logical CPUs={Environment.ProcessorCount}; serverGC={System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine($"BENCH iterations=600000; warmup={warmup} each; samples={samples} each; Stopwatch frequency={Stopwatch.Frequency}");
        foreach (var entry in new[] { ("create", create), ("verify", verify) })
        {
            entry.Item2.Sort(); var values = entry.Item2;
            Console.WriteLine(FormattableString.Invariant($"BENCH {entry.Item1}: min={values[0]:F3} ms; median={(values[4]+values[5])/2:F3} ms; max={values[^1]:F3} ms"));
        }
    }
}
