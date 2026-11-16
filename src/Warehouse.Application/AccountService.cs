using Warehouse.Domain;

namespace Warehouse.Application;

public sealed record SignInResult(Guid UserId, string Login, Role Role);

public sealed class AccountService(IUnitOfWork work, ICurrentUser user, IClock clock, IPasswordHasher passwords)
    : ServiceBase(work, user, clock)
{
    public SignInResult SignIn(string login, string password)
    {
        var normalized = Rules.Text(login);
        Rules.Require(!string.IsNullOrEmpty(password), ErrorCode.Validation, "Password is required.");
        // Commit failed attempts before reporting the authentication error to the caller.
        var outcome = Transaction(s =>
        {
            var account = s.Users.ByLogin(normalized);
            if (account == null)
            {
                s.Audit.Add(new AuditEntry(Guid.NewGuid(), null, "UnknownLoginFailed", null, Clock.UtcNow));
                return (Account: (UserAccount?)null, Error: (ErrorCode?)ErrorCode.InvalidCredentials);
            }
            if (!account.IsEnabled || account.IsLocked)
            {
                s.Audit.Add(new AuditEntry(Guid.NewGuid(), account.Id, "LockedLoginRejected", account.Id, Clock.UtcNow));
                return (Account: (UserAccount?)null, Error: (ErrorCode?)ErrorCode.AccountLocked);
            }
            if (!passwords.Verify(password, account.PasswordHash))
            {
                var failed = account.FailedLogin();
                s.Users.Update(failed);
                s.Audit.Add(new AuditEntry(Guid.NewGuid(), account.Id, "LoginFailed", account.Id, Clock.UtcNow));
                return (Account: (UserAccount?)null, Error: (ErrorCode?)(failed.IsLocked ? ErrorCode.AccountLocked : ErrorCode.InvalidCredentials));
            }
            var authenticated = account.ResetFailures();
            s.Users.Update(authenticated);
            s.Audit.Add(new AuditEntry(Guid.NewGuid(), account.Id, "LoginSucceeded", account.Id, Clock.UtcNow));
            return (Account: (UserAccount?)authenticated, Error: (ErrorCode?)null);
        });
        if (outcome.Error is { } error) throw new WarehouseException(error, "Sign-in failed.");
        var result = outcome.Account!;
        return new SignInResult(result.Id, result.Login, result.Role);
    }

    public UserAccount CreateUser(string login, string password, Role role) => Run(Permission.ManageUsers, s =>
    {
        Rules.Require(!string.IsNullOrWhiteSpace(password), ErrorCode.Validation, "Password is required.");
        var normalized = Rules.Text(login);
        Rules.Require(s.Users.ByLogin(normalized) == null, ErrorCode.Duplicate, "Login already exists.");
        var account = new UserAccount(Guid.NewGuid(), normalized, passwords.Hash(password), role);
        s.Users.Add(account);
        Audit(s, "UserCreated", account.Id);
        return account;
    });

    public UserAccount UpdateUser(Guid id, Role role, bool enabled) => Run(Permission.ManageUsers, s =>
    {
        var previous = Get(s.Users, id);
        Rules.Defined(role);
        Rules.Require(id != ActorId || role == Role.Administrator && enabled, ErrorCode.InvalidState, "An administrator cannot remove their own access.");
        var account = new UserAccount(id, previous.Login, previous.PasswordHash, role, previous.FailedAttempts, enabled);
        s.Users.Update(account);
        Audit(s, "UserUpdated", id);
        return account;
    });

    public UserAccount Unlock(Guid id) => Run(Permission.ManageUsers, s =>
    {
        var account = Get(s.Users, id).ResetFailures();
        s.Users.Update(account);
        Audit(s, "UserUnlocked", id);
        return account;
    });

    public void ChangePassword(string currentPassword, string newPassword) => Run(Permission.Read, s =>
    {
        Rules.Require(!string.IsNullOrWhiteSpace(newPassword), ErrorCode.Validation, "Password is required.");
        var account = Get(s.Users, ActorId!.Value);
        Rules.Require(!string.IsNullOrEmpty(currentPassword) && passwords.Verify(currentPassword, account.PasswordHash),
            ErrorCode.InvalidCredentials, "Current password is incorrect.");
        s.Users.Update(new UserAccount(account.Id, account.Login, passwords.Hash(newPassword), account.Role, account.FailedAttempts, account.IsEnabled));
        Audit(s, "PasswordChanged", account.Id);
        return true;
    });

    public UserAccount ResetPassword(Guid id, string newPassword) => Run(Permission.ManageUsers, s =>
    {
        Rules.Require(!string.IsNullOrWhiteSpace(newPassword), ErrorCode.Validation, "Password is required.");
        var old = Get(s.Users, id);
        var account = new UserAccount(old.Id, old.Login, passwords.Hash(newPassword), old.Role, 0, old.IsEnabled);
        s.Users.Update(account);
        Audit(s, "PasswordReset", id);
        return account;
    });

    public IReadOnlyList<AuditEntry> AuditLog(DateTimeOffset from, DateTimeOffset to) => Run(Permission.ReadAudit, s =>
    {
        Rules.Require(from <= to, ErrorCode.Validation, "Invalid date range.");
        return s.Audit.All().Where(x => x.OccurredAt >= from && x.OccurredAt <= to).OrderBy(x => x.OccurredAt).ToArray();
    });
}
