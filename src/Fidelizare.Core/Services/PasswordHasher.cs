using System.Security.Cryptography;

namespace Fidelizare.Core.Services;

/// <summary>
/// Amprentarea parolelor cu PBKDF2-HMAC-SHA256.
///
/// Parolele nu se păstrează niciodată în clar și nici criptate reversibil: dacă
/// fișierul bazei de date ajunge pe mâna cuiva, parolele personalului nu trebuie
/// să poată fi recuperate. Numărul de iterații este salvat odată cu amprenta,
/// ca să poată fi mărit în viitor fără a invalida conturile existente.
/// </summary>
public static class PasswordHasher
{
    /// <summary>Iterațiile folosite pentru parolele noi.</summary>
    public const int DefaultIterations = 210_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static (string Hash, string Salt, int Iterations) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, DefaultIterations);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), DefaultIterations);
    }

    /// <summary>
    /// Compară parola dată cu amprenta stocată. Comparația este în timp constant,
    /// ca durata răspunsului să nu spună nimic despre cât de aproape a fost parola.
    /// </summary>
    public static bool Verify(string password, string storedHash, string storedSalt, int iterations)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
            return false;

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(storedSalt);
            expected = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (iterations <= 0)
            return false;

        var actual = Derive(password, salt, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);

    /// <summary>Regulile minime de parolă, verificate la creare și la schimbare.</summary>
    public static string? ValidateStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Parola nu poate fi goală.";

        if (password.Length < 8)
            return "Parola trebuie să aibă cel puțin 8 caractere.";

        if (password.Length > 128)
            return "Parola este prea lungă.";

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);

        if (!hasLetter || !hasDigit)
            return "Parola trebuie să conțină cel puțin o literă și o cifră.";

        return null;
    }
}
