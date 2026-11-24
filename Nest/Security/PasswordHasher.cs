using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nest.Security;

public enum PasswordVerificationResult
{
    InvalidCredential,
    Failed,
    Success,
    SuccessRehashNeeded
}

/// <summary>Standalone v1 credential codec. Never identifies or verifies legacy plaintext.
/// Migration must dispatch using a separate, explicit storage-format marker.</summary>
public static class PasswordHasher
{
    public const int CurrentIterations = 600_000;
    // Verification compatibility limits, not selectable parameters for new hashes.
    public const int MinimumAcceptedIterations = 100_000;
    public const int MaximumAcceptedIterations = 1_200_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;
    public const int MaximumCredentialLength = 255;
    private const string Prefix = "$nest$pbkdf2-sha256$v1$";
    // Reject unpaired UTF-16 surrogates instead of silently replacing distinct inputs.
    private static readonly Encoding PasswordEncoding = new UTF8Encoding(false, true);

    /// <summary>Accepts empty/long passwords; password policy belongs to the caller.
    /// Null or ill-formed UTF-16 throws. No trimming, normalization or truncation.</summary>
    public static string HashPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[]? passwordBytes = null;
        byte[]? salt = null;
        byte[]? hash = null;
        try
        {
            passwordBytes = PasswordEncoding.GetBytes(password);
            salt = RandomNumberGenerator.GetBytes(SaltSize);
            hash = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, CurrentIterations, HashAlgorithmName.SHA256, HashSize);
            return Prefix + CurrentIterations.ToString(CultureInfo.InvariantCulture) + "$" +
                Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }
        finally
        {
            Clear(passwordBytes); Clear(salt); Clear(hash);
        }
    }

    /// <summary>Malformed/unsupported credentials never reach PBKDF2. A successful
    /// verification is required before acting on a rehash recommendation.</summary>
    public static PasswordVerificationResult VerifyPassword(string password, string? credential)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!TryParse(credential, out var iterations, out var salt, out var expected))
            return PasswordVerificationResult.InvalidCredential;
        byte[]? passwordBytes = null;
        byte[]? actual = null;
        try
        {
            passwordBytes = PasswordEncoding.GetBytes(password);
            actual = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, HashSize);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                return PasswordVerificationResult.Failed;
            return iterations < CurrentIterations ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success;
        }
        finally
        {
            Clear(passwordBytes); Clear(actual); Clear(salt); Clear(expected);
        }
    }

    /// <summary>Metadata inspection only, not authentication. Throws FormatException
    /// for invalid credentials; never downgrade a supported higher work factor.</summary>
    public static bool NeedsRehash(string? credential)
    {
        if (!TryParse(credential, out var iterations, out var salt, out var hash))
            throw new FormatException("Invalid or unsupported password credential.");
        try { return iterations < CurrentIterations; }
        finally { Clear(salt); Clear(hash); }
    }

    private static bool TryParse(string? credential, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0; salt = Array.Empty<byte>(); hash = Array.Empty<byte>();
        // Bound allocation before Split/Base64, then require exact ordinal format.
        if (credential is null || credential.Length > MaximumCredentialLength) return false;
        var fields = credential.Split('$');
        if (fields.Length != 7 || fields[0] != "" || fields[1] != "nest" ||
            fields[2] != "pbkdf2-sha256" || fields[3] != "v1") return false;
        var count = fields[4];
        if (count.Length is < 6 or > 7 || count[0] == '0' || count.Any(c => c < '0' || c > '9') ||
            !int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out iterations) ||
            iterations < MinimumAcceptedIterations || iterations > MaximumAcceptedIterations ||
            fields[5].Length != 24 || fields[6].Length != 44) return false;
        var decodedSalt = new byte[SaltSize];
        var decodedHash = new byte[HashSize];
        bool valid = false;
        try
        {
            valid = Convert.TryFromBase64String(fields[5], decodedSalt, out var saltLength) && saltLength == SaltSize &&
                Convert.TryFromBase64String(fields[6], decodedHash, out var hashLength) && hashLength == HashSize &&
                // Reject whitespace, noncanonical padding and unused-bit variants.
                Convert.ToBase64String(decodedSalt) == fields[5] && Convert.ToBase64String(decodedHash) == fields[6];
            if (!valid) return false;
            salt = decodedSalt; hash = decodedHash; return true;
        }
        finally { if (!valid) { Clear(decodedSalt); Clear(decodedHash); } }
    }

    private static void Clear(byte[]? bytes)
    {
        if (bytes != null) CryptographicOperations.ZeroMemory(bytes);
    }
}
