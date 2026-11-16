namespace Warehouse.Domain;

public enum Role { Administrator, Storekeeper, Auditor }
public enum Permission { Read, ManageCatalog, ManageWarehouses, ManageDocuments, ReverseDocuments, Inventory, ManageUsers, ReadAudit }

public static class AccessPolicy
{
    public static bool Allows(Role role, Permission permission) => Enum.IsDefined(role) && Enum.IsDefined(permission) &&
        (role == Role.Administrator || permission == Permission.Read ||
         role == Role.Storekeeper && permission is Permission.ManageCatalog or Permission.ManageDocuments ||
         role == Role.Auditor && permission is Permission.Inventory or Permission.ReadAudit);
}

public sealed record UserAccount
{
    public const int FailureLimit = 5;
    public Guid Id { get; }
    public string Login { get; }
    public string PasswordHash { get; }
    public Role Role { get; }
    public int FailedAttempts { get; }
    public bool IsEnabled { get; }
    public bool IsLocked => FailedAttempts >= FailureLimit;

    public UserAccount(Guid id, string login, string passwordHash, Role role, int failedAttempts = 0, bool isEnabled = true)
    {
        Id = Rules.Id(id);
        Login = Rules.Text(login);
        PasswordHash = Rules.Text(passwordHash);
        Role = Rules.Defined(role);
        Rules.Require(failedAttempts is >= 0 and <= FailureLimit, ErrorCode.Validation, "Invalid login failure count.");
        FailedAttempts = failedAttempts;
        IsEnabled = isEnabled;
    }

    public UserAccount FailedLogin() => new(Id, Login, PasswordHash, Role, Math.Min(FailureLimit, FailedAttempts + 1), IsEnabled);
    public UserAccount ResetFailures() => new(Id, Login, PasswordHash, Role, 0, IsEnabled);
}

public sealed record AuditEntry
{
    public Guid Id { get; }
    public Guid? ActorId { get; }
    public string Action { get; }
    public Guid? EntityId { get; }
    public DateTimeOffset OccurredAt { get; }

    public AuditEntry(Guid id, Guid? actorId, string action, Guid? entityId, DateTimeOffset occurredAt)
    {
        Id = Rules.Id(id);
        ActorId = actorId is { } actor ? Rules.Id(actor) : null;
        Action = Rules.Text(action);
        EntityId = entityId is { } entity ? Rules.Id(entity) : null;
        OccurredAt = occurredAt;
    }
}
