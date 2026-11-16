namespace Warehouse.Tests;

public sealed class AccountTests
{
    [Fact]
    public void Fifth_failure_locks_login_and_correct_password_cannot_bypass_lock()
    {
        var w = new World();
        w.Identity.UserId = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Check.Error(attempt == 5 ? ErrorCode.AccountLocked : ErrorCode.InvalidCredentials, () => w.Accounts.SignIn(" KEEPER ", "wrong"));
            Assert.Equal(attempt, Assert.Single(w.Read(s => s.Users.All()), x => x.Id == w.Keeper).FailedAttempts);
        }
        Check.Error(ErrorCode.AccountLocked, () => w.Accounts.SignIn("keeper", "password"));
        Assert.Equal(5, Assert.Single(w.Read(s => s.Users.All()), x => x.Id == w.Keeper).FailedAttempts);
        Assert.Equal(5, w.Read(s => s.Audit.All()).Count(x => x.Action == "LoginFailed"));
        w.Identity.UserId = w.Admin;
        Assert.False(w.Accounts.Unlock(w.Keeper).IsLocked);
        Assert.Equal(w.Keeper, w.Accounts.SignIn("keeper", "password").UserId);
    }

    [Fact]
    public void Successful_login_resets_failures_and_returns_only_identity_information()
    {
        var w = new World();
        for (var i = 0; i < 4; i++) Check.Error(ErrorCode.InvalidCredentials, () => w.Accounts.SignIn("keeper", "bad"));
        var result = w.Accounts.SignIn("keeper", "password");
        Assert.Equal(new SignInResult(w.Keeper, "keeper", Role.Storekeeper), result);
        Assert.Equal(0, Assert.Single(w.Read(s => s.Users.All()), x => x.Id == w.Keeper).FailedAttempts);
        Check.Error(ErrorCode.InvalidCredentials, () => w.Accounts.SignIn("keeper", "bad"));
        Assert.Equal(1, Assert.Single(w.Read(s => s.Users.All()), x => x.Id == w.Keeper).FailedAttempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_login_and_new_password_inputs_are_rejected(string? input)
    {
        var w = new World();
        Check.Error(ErrorCode.Validation, () => w.Accounts.SignIn(input!, "password"));
        Check.Error(ErrorCode.Validation, () => w.Accounts.CreateUser(input!, "password", Role.Auditor));
        Check.Error(ErrorCode.Validation, () => w.Accounts.CreateUser("new", input!, Role.Auditor));
        Check.Error(ErrorCode.Validation, () => w.Accounts.ResetPassword(w.Keeper, input!));
        Check.Error(ErrorCode.Validation, () => w.Accounts.ChangePassword("password", input!));
    }

    [Fact]
    public void Unknown_disabled_and_empty_password_logins_do_not_change_other_accounts()
    {
        var w = new World();
        Check.Error(ErrorCode.InvalidCredentials, () => w.Accounts.SignIn("unknown", "password"));
        Check.Error(ErrorCode.Validation, () => w.Accounts.SignIn("keeper", ""));
        Check.Error(ErrorCode.Validation, () => w.Accounts.SignIn("keeper", null!));
        w.Accounts.UpdateUser(w.Keeper, Role.Storekeeper, false);
        Check.Error(ErrorCode.AccountLocked, () => w.Accounts.SignIn("keeper", "password"));
        Assert.Equal(0, Assert.Single(w.Read(s => s.Users.All()), x => x.Id == w.Keeper).FailedAttempts);
        Assert.Contains(w.Read(s => s.Audit.All()), x => x.Action == "UnknownLoginFailed" && x.ActorId == null);
    }

    [Fact]
    public void User_management_enforces_unique_logins_roles_and_self_access()
    {
        var w = new World();
        var account = w.Accounts.CreateUser(" new ", "pw", Role.Auditor);
        Assert.Equal("new", account.Login);
        Assert.Equal(w.Passwords.Hash("pw"), account.PasswordHash);
        Check.Error(ErrorCode.Duplicate, () => w.Accounts.CreateUser(" NEW ", "pw", Role.Auditor));
        Check.Error(ErrorCode.Validation, () => w.Accounts.CreateUser("invalid", "pw", (Role)99));
        Check.Error(ErrorCode.Validation, () => w.Accounts.UpdateUser(account.Id, (Role)99, true));
        Check.Error(ErrorCode.InvalidState, () => w.Accounts.UpdateUser(w.Admin, Role.Auditor, true));
        Check.Error(ErrorCode.InvalidState, () => w.Accounts.UpdateUser(w.Admin, Role.Administrator, false));
        Assert.Equal(Role.Administrator, w.Accounts.UpdateUser(w.Admin, Role.Administrator, true).Role);
        Assert.Equal(Role.Storekeeper, w.Accounts.UpdateUser(account.Id, Role.Storekeeper, true).Role);
        w.Accounts.UpdateUser(account.Id, Role.Storekeeper, false);
        Check.Error(ErrorCode.AccountLocked, () => w.Accounts.SignIn("new", "pw"));
        w.Accounts.UpdateUser(account.Id, Role.Auditor, true);
        Assert.Equal(Role.Auditor, w.Accounts.SignIn("new", "pw").Role);
    }

    [Fact]
    public void Password_change_validates_old_password_and_preserves_whitespace_in_passwords()
    {
        var w = new World();
        w.Identity.UserId = w.Keeper;
        Check.Error(ErrorCode.InvalidCredentials, () => w.Accounts.ChangePassword("bad", "new"));
        Check.Error(ErrorCode.InvalidCredentials, () => w.Accounts.ChangePassword(null!, "new"));
        w.Accounts.ChangePassword("password", " new ");
        Check.Error(ErrorCode.InvalidCredentials, () => w.Accounts.SignIn("keeper", "new"));
        Assert.Equal(w.Keeper, w.Accounts.SignIn("keeper", " new ").UserId);
        w.Identity.UserId = w.Admin;
        w.Accounts.ResetPassword(w.Keeper, "reset");
        Assert.Equal(w.Keeper, w.Accounts.SignIn("keeper", "reset").UserId);
    }

    [Fact]
    public void Reset_password_unlocks_but_does_not_enable_a_disabled_account()
    {
        var w = new World();
        for (var i = 0; i < 5; i++) Assert.Throws<WarehouseException>(() => w.Accounts.SignIn("keeper", "bad"));
        w.Accounts.UpdateUser(w.Keeper, Role.Storekeeper, false);
        var reset = w.Accounts.ResetPassword(w.Keeper, "new");
        Assert.False(reset.IsLocked);
        Assert.False(reset.IsEnabled);
        Check.Error(ErrorCode.AccountLocked, () => w.Accounts.SignIn("keeper", "new"));
    }

    [Fact]
    public void Audit_dates_are_inclusive_and_missing_users_have_no_side_effects()
    {
        var w = new World();
        var atStart = w.Accounts.AuditLog(w.Clock.UtcNow, w.Clock.UtcNow).Count;
        Assert.Equal(4, atStart);
        w.Clock.UtcNow = w.Clock.UtcNow.AddSeconds(1);
        w.Accounts.CreateUser("new", "p", Role.Auditor);
        var latest = Assert.Single(w.Accounts.AuditLog(w.Clock.UtcNow, w.Clock.UtcNow));
        Assert.Equal("UserCreated", latest.Action);
        Assert.Equal(w.Admin, latest.ActorId);
        Assert.Empty(w.Accounts.AuditLog(w.Clock.UtcNow.AddTicks(1), w.Clock.UtcNow.AddSeconds(1)));
        Check.Error(ErrorCode.Validation, () => w.Accounts.AuditLog(w.Clock.UtcNow, w.Clock.UtcNow.AddTicks(-1)));
        var missing = Guid.NewGuid();
        Check.Error(ErrorCode.NotFound, () => w.Accounts.UpdateUser(missing, Role.Auditor, true));
        Check.Error(ErrorCode.NotFound, () => w.Accounts.Unlock(missing));
        Check.Error(ErrorCode.NotFound, () => w.Accounts.ResetPassword(missing, "p"));
    }
}
