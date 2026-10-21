using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;

namespace Fidelizare.Core.Services;

/// <summary>
/// Autentificarea și gestiunea conturilor de personal.
///
/// Programul ține evidența unor beneficii cu valoare reală, deci trebuie să se
/// știe cine a acordat ce. Conturile separate pe casier nu sunt birocrație:
/// fără ele, registrul de audit nu ar spune nimic util.
/// </summary>
public sealed class AuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly IUnitOfWorkFactory _factory;
    private readonly IClock _clock;

    /// <summary>
    /// Încercările eșuate, ținute în memorie. Nu se salvează în baza de date
    /// intenționat: blocarea trebuie să încetinească o încercare de ghicire în
    /// sesiunea curentă, nu să lase un casier blocat definitiv după o repornire.
    /// </summary>
    private readonly Dictionary<string, (int Count, DateTime LastAttempt)> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);

    public AuthService(IUnitOfWorkFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public OperationResult<AppUser> Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return OperationResult<AppUser>.Fail("Introdu numele de utilizator.");

        username = username.Trim();

        if (IsLockedOut(username, out var remaining))
            return OperationResult<AppUser>.Fail(
                $"Prea multe încercări eșuate. Mai încearcă peste {remaining.Minutes} min {remaining.Seconds} sec.");

        using var uow = _factory.Create();
        var user = uow.Users.GetByUsername(username);

        // Mesaj identic pentru utilizator inexistent și parolă greșită: altfel
        // s-ar putea afla care conturi există.
        if (user is null ||
            !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
        {
            RegisterFailure(username);
            return OperationResult<AppUser>.Fail("Nume de utilizator sau parolă incorecte.");
        }

        if (!user.IsActive)
        {
            RegisterFailure(username);
            return OperationResult<AppUser>.Fail("Contul este dezactivat. Contactează administratorul.");
        }

        _failedAttempts.Remove(username);

        user.LastLoginAt = _clock.Now;
        uow.Users.Update(user);
        uow.Commit();

        return OperationResult<AppUser>.Ok(user);
    }

    public OperationResult ChangePassword(int userId, string currentPassword, string newPassword)
    {
        var strength = PasswordHasher.ValidateStrength(newPassword);
        if (strength is not null)
            return OperationResult.Fail(strength);

        using var uow = _factory.Create();

        var user = uow.Users.GetById(userId);
        if (user is null)
            return OperationResult.Fail("Contul nu a fost găsit.");

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
            return OperationResult.Fail("Parola actuală este incorectă.");

        if (PasswordHasher.Verify(newPassword, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
            return OperationResult.Fail("Noua parolă trebuie să fie diferită de cea actuală.");

        ApplyPassword(user, newPassword);
        user.MustChangePassword = false;
        uow.Users.Update(user);

        uow.Audit.Insert(new AuditEntry
        {
            OccurredAt = _clock.Now,
            UserId = user.Id,
            UserName = user.Username,
            Action = "PasswordChanged",
            EntityType = "User",
            EntityId = user.Id,
            Details = "Parolă schimbată de titularul contului.",
        });

        uow.Commit();
        return OperationResult.Ok();
    }

    public IReadOnlyList<AppUser> GetUsers()
    {
        using var uow = _factory.Create();
        return uow.Users.GetAll();
    }

    public OperationResult<AppUser> CreateUser(AppUser user, string password, AppUser actor)
    {
        if (!actor.CanConfigureProgram)
            return OperationResult<AppUser>.Fail("Doar administratorul poate crea conturi.");

        var error = ValidateUser(user) ?? PasswordHasher.ValidateStrength(password);
        if (error is not null)
            return OperationResult<AppUser>.Fail(error);

        using var uow = _factory.Create();

        user.Username = user.Username.Trim();

        if (uow.Users.UsernameExists(user.Username))
            return OperationResult<AppUser>.Fail($"Utilizatorul „{user.Username}” există deja.");

        ApplyPassword(user, password);
        user.CreatedAt = _clock.Now;
        user.Id = uow.Users.Insert(user);

        uow.Audit.Insert(new AuditEntry
        {
            OccurredAt = _clock.Now,
            UserId = actor.Id,
            UserName = actor.Username,
            Action = "UserCreated",
            EntityType = "User",
            EntityId = user.Id,
            Details = $"{user.Username} ({user.RoleLabel})",
        });

        uow.Commit();
        return OperationResult<AppUser>.Ok(user);
    }

    public OperationResult UpdateUser(AppUser user, AppUser actor)
    {
        if (!actor.CanConfigureProgram)
            return OperationResult.Fail("Doar administratorul poate modifica conturi.");

        var error = ValidateUser(user);
        if (error is not null)
            return OperationResult.Fail(error);

        using var uow = _factory.Create();

        var existing = uow.Users.GetById(user.Id);
        if (existing is null)
            return OperationResult.Fail("Contul nu a fost găsit.");

        user.Username = user.Username.Trim();

        if (uow.Users.UsernameExists(user.Username, user.Id))
            return OperationResult.Fail($"Utilizatorul „{user.Username}” există deja.");

        // Nu se permite rămânerea fără niciun administrator activ: aplicația ar
        // deveni neconfigurabilă și ar trebui intervenit direct în baza de date.
        var losesAdmin = existing is { Role: UserRole.Admin, IsActive: true } &&
                         (user.Role != UserRole.Admin || !user.IsActive);

        if (losesAdmin && uow.Users.CountActiveAdmins(exceptUserId: user.Id) == 0)
            return OperationResult.Fail("Trebuie să rămână cel puțin un administrator activ.");

        // Parola se schimbă doar prin metodele dedicate.
        user.PasswordHash = existing.PasswordHash;
        user.PasswordSalt = existing.PasswordSalt;
        user.PasswordIterations = existing.PasswordIterations;
        user.CreatedAt = existing.CreatedAt;
        user.LastLoginAt = existing.LastLoginAt;

        uow.Users.Update(user);

        uow.Audit.Insert(new AuditEntry
        {
            OccurredAt = _clock.Now,
            UserId = actor.Id,
            UserName = actor.Username,
            Action = "UserUpdated",
            EntityType = "User",
            EntityId = user.Id,
            Details = $"{user.Username}: rol {existing.RoleLabel} → {user.RoleLabel}, " +
                      $"activ {existing.IsActive} → {user.IsActive}",
        });

        uow.Commit();
        return OperationResult.Ok();
    }

    /// <summary>Resetează parola altui utilizator; acesta va fi obligat să o schimbe la următoarea intrare.</summary>
    public OperationResult ResetPassword(int userId, string newPassword, AppUser actor)
    {
        if (!actor.CanConfigureProgram)
            return OperationResult.Fail("Doar administratorul poate reseta parole.");

        var strength = PasswordHasher.ValidateStrength(newPassword);
        if (strength is not null)
            return OperationResult.Fail(strength);

        using var uow = _factory.Create();

        var user = uow.Users.GetById(userId);
        if (user is null)
            return OperationResult.Fail("Contul nu a fost găsit.");

        ApplyPassword(user, newPassword);
        user.MustChangePassword = true;
        uow.Users.Update(user);

        uow.Audit.Insert(new AuditEntry
        {
            OccurredAt = _clock.Now,
            UserId = actor.Id,
            UserName = actor.Username,
            Action = "PasswordReset",
            EntityType = "User",
            EntityId = user.Id,
            Details = $"Parolă resetată pentru {user.Username}.",
        });

        uow.Commit();
        _failedAttempts.Remove(user.Username);
        return OperationResult.Ok();
    }

    private static void ApplyPassword(AppUser user, string password)
    {
        var (hash, salt, iterations) = PasswordHasher.Create(password);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.PasswordIterations = iterations;
    }

    private static string? ValidateUser(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Username))
            return "Numele este obligatoriu.";

        var name = user.Username.Trim();

        if (name.Length < 3)
            return "Numele trebuie să aibă cel puțin 3 caractere.";

        if (name.Length > 60)
            return "Numele este prea lung.";

        // Spațiile sunt permise: numele este acum și cel afișat în rapoarte, deci
        // „Ana Popescu” trebuie să fie o valoare validă, nu doar „ana”.
        return null;
    }

    private bool IsLockedOut(string username, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (!_failedAttempts.TryGetValue(username, out var entry))
            return false;

        if (entry.Count < MaxFailedAttempts)
            return false;

        var elapsed = _clock.Now - entry.LastAttempt;
        if (elapsed >= LockoutDuration)
        {
            _failedAttempts.Remove(username);
            return false;
        }

        remaining = LockoutDuration - elapsed;
        return true;
    }

    private void RegisterFailure(string username)
    {
        var count = _failedAttempts.TryGetValue(username, out var entry) ? entry.Count + 1 : 1;
        _failedAttempts[username] = (count, _clock.Now);
    }
}
