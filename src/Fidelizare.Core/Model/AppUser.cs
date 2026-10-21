namespace Fidelizare.Core.Model;

/// <summary>
/// Un utilizator al aplicației: casier, responsabil de magazin sau administrator.
/// Parola nu se păstrează niciodată în clar, doar amprenta PBKDF2 și sarea.
/// </summary>
public sealed class AppUser
{
    public int Id { get; set; }

    /// <summary>
    /// Numele persoanei. Este și numele de autentificare, și cel care apare în
    /// rapoarte și în jurnalul de audit.
    ///
    /// Un singur nume, nu două: la un magazin cu câțiva angajați, separarea între
    /// un identificator scurt și un nume afișat însemna două câmpuri de completat
    /// pentru fiecare cont și o întrebare la fiecare primă autentificare.
    /// Autentificarea se face o dată pe tură, deci scurtimea nu valora nimic.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>Numărul de iterații folosit la generarea amprentei, ca să se poată mări în timp.</summary>
    public int PasswordIterations { get; set; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Obligă schimbarea parolei la prima autentificare. Pornit pentru contul implicit.</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public bool CanManagePoints => Role is UserRole.Manager or UserRole.Admin;

    public bool CanConfigureProgram => Role is UserRole.Admin;

    public string RoleLabel => Role switch
    {
        UserRole.Cashier => "Casier",
        UserRole.Manager => "Responsabil",
        UserRole.Admin => "Administrator",
        _ => Role.ToString(),
    };
}
