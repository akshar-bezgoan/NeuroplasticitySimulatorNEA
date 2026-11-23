using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Neuroplasticity.Data;

/// <summary>Password complexity checker and strength indicator (spec 2.6.2).</summary>
public static class PasswordPolicy
{
    static readonly string[] Common = { "password", "qwerty", "letmein", "welcome", "admin", "iloveyou", "123456", "abc123", "monkey", "dragon" };

    /// <returns>score 0-5 and the list of rules still unmet</returns>
    public static (int score, List<string> problems) Check(string pw)
    {
        var problems = new List<string>(); int score = 0;
        if (pw.Length >= 12) score++; else problems.Add("at least 12 characters");
        if (pw.Any(char.IsUpper)) score++; else problems.Add("an uppercase letter");
        if (pw.Any(char.IsLower)) score++; else problems.Add("a lowercase letter");
        if (pw.Any(char.IsDigit)) score++; else problems.Add("a number");
        if (pw.Any(ch => !char.IsLetterOrDigit(ch))) score++; else problems.Add("a special character");
        string lower = pw.ToLowerInvariant();
        if (Common.Any(lower.Contains)) { problems.Add("nothing common like \"password\" or \"123456\""); score = Math.Max(0, score - 2); }
        return (score, problems);
    }
}

/// <summary>Argon2id hashing with a random per-user salt. Stored as argon2id$m$t$p$salt$hash.</summary>
public static class Hasher
{
    const int Mem = 19456, Iter = 2, Par = 2;   // OWASP minimum-recommended Argon2id settings

    public static string Hash(string pw)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return $"argon2id${Mem}${Iter}${Par}${Convert.ToBase64String(salt)}${Convert.ToBase64String(Derive(pw, salt, Mem, Iter, Par))}";
    }

    public static bool Verify(string pw, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 6 || p[0] != "argon2id") return false;
        var expected = Convert.FromBase64String(p[5]);
        var actual = Derive(pw, Convert.FromBase64String(p[4]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    static byte[] Derive(string pw, byte[] salt, int mem, int iter, int par) =>
        new Argon2id(Encoding.UTF8.GetBytes(pw)) { Salt = salt, MemorySize = mem, Iterations = iter, DegreeOfParallelism = par }.GetBytes(32);
}

public static class AuthService
{
    public static string Register(Database db, string username, string email, string pw, string confirm)
    {
        username = username.Trim();
        if (username.Length < 3) return "Username must be at least 3 characters.";
        if (pw != confirm) return "Passwords do not match.";
        var (_, problems) = PasswordPolicy.Check(pw);
        if (problems.Count > 0) return "Password needs: " + string.Join(", ", problems) + ".";
        return db.CreateUser(username, email, Hasher.Hash(pw)) ? null : "That username is already taken.";
    }

    public static UserRecord Login(Database db, string username, string pw)
    {
        var f = db.FindUser(username.Trim());
        return f.HasValue && Hasher.Verify(pw, f.Value.hash) ? f.Value.user : null;   // same result for unknown user / wrong password
    }
}
