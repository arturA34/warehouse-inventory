namespace Warehouse.Tests;

public sealed class SecurityTests
{
    public static IEnumerable<object[]> PermissionCases()
    {
        // Expected role matrix is specified independently of AccessPolicy.
        var matrix = new Dictionary<Role, Permission[]>
        {
            [Role.Administrator] = [Permission.Read, Permission.ManageCatalog, Permission.ManageWarehouses, Permission.ManageDocuments,
                Permission.ReverseDocuments, Permission.Inventory, Permission.ManageUsers, Permission.ReadAudit],
            [Role.Storekeeper] = [Permission.Read, Permission.ManageCatalog, Permission.ManageDocuments],
            [Role.Auditor] = [Permission.Read, Permission.Inventory, Permission.ReadAudit]
        };
        foreach (var role in Enum.GetValues<Role>())
            foreach (var permission in Enum.GetValues<Permission>())
                yield return [role, permission, matrix[role].Contains(permission)];
    }

    [Theory]
    [MemberData(nameof(PermissionCases))]
    public void Every_role_permission_pair_has_an_explicit_expectation(Role role, Permission permission, bool expected) =>
        Assert.Equal(expected, AccessPolicy.Allows(role, permission));

    [Fact]
    public void Invalid_enums_are_rejected_without_privileges()
    {
        Assert.False(AccessPolicy.Allows((Role)999, Permission.Read));
        Assert.False(AccessPolicy.Allows(Role.Administrator, (Permission)999));
        Check.Error(ErrorCode.Validation, () => new UserAccount(Guid.NewGuid(), "l", "h", (Role)999));
        Check.Error(ErrorCode.Validation, () => new UserAccount(Guid.NewGuid(), "l", "h", Role.Auditor, -1));
        Check.Error(ErrorCode.Validation, () => new UserAccount(Guid.NewGuid(), "l", "h", Role.Auditor, 6));
        var locked = new UserAccount(Guid.NewGuid(), "l", "h", Role.Auditor, 5);
        Assert.True(locked.IsLocked);
        Assert.Equal(5, locked.FailedLogin().FailedAttempts);
        Assert.False(locked.ResetFailures().IsLocked);
    }

    public static IEnumerable<object[]> ProtectedOperations()
    {
        var operations = new (string, Permission, Action<World>)[]
        {
            ("create product", Permission.ManageCatalog, w => w.Catalog.CreateProduct("x", "x", "x", "x")),
            ("update product", Permission.ManageCatalog, w => w.Catalog.UpdateProduct(w.P.Id, "P", "00123", "n", "pcs")),
            ("archive product", Permission.ManageCatalog, w => w.Catalog.SetProductActive(w.P.Id, false)),
            ("get product", Permission.Read, w => w.Catalog.GetProduct(w.P.Id)),
            ("barcode", Permission.Read, w => w.Catalog.FindByBarcode("00123")),
            ("search", Permission.Read, w => w.Catalog.Search()),
            ("create warehouse", Permission.ManageWarehouses, w => w.Catalog.CreateWarehouse("C")),
            ("update warehouse", Permission.ManageWarehouses, w => w.Catalog.UpdateWarehouse(w.A.Id, "AA")),
            ("list warehouses", Permission.Read, w => w.Catalog.Warehouses()),
            ("minimum", Permission.ManageCatalog, w => w.Catalog.SetMinimum(w.A.Id, w.P.Id, 1)),
            ("create document", Permission.ManageDocuments, w => w.Documents.Create(DocumentKind.Issue, w.A.Id, [])),
            ("edit document", Permission.ManageDocuments, w => w.Documents.Edit(Guid.Empty, [])),
            ("post document", Permission.ManageDocuments, w => w.Documents.Post(Guid.Empty)),
            ("reverse document", Permission.ReverseDocuments, w => w.Documents.Reverse(Guid.Empty)),
            ("get document", Permission.Read, w => w.Documents.GetDocument(Guid.Empty)),
            ("list documents", Permission.Read, w => w.Documents.List()),
            ("start inventory", Permission.Inventory, w => w.Inventories.Start(w.A.Id, [w.P.Id])),
            ("count inventory", Permission.Inventory, w => w.Inventories.Count(Guid.Empty, w.P.Id, 0)),
            ("scan inventory", Permission.Inventory, w => w.Inventories.Scan(Guid.Empty, "00123")),
            ("complete inventory", Permission.Inventory, w => w.Inventories.Complete(Guid.Empty)),
            ("cancel inventory", Permission.Inventory, w => w.Inventories.Cancel(Guid.Empty)),
            ("get inventory", Permission.Read, w => w.Inventories.GetInventory(Guid.Empty)),
            ("list inventories", Permission.Read, w => w.Inventories.List()),
            ("create user", Permission.ManageUsers, w => w.Accounts.CreateUser("new", "password", Role.Auditor)),
            ("update user", Permission.ManageUsers, w => w.Accounts.UpdateUser(w.Keeper, Role.Storekeeper, true)),
            ("unlock user", Permission.ManageUsers, w => w.Accounts.Unlock(w.Keeper)),
            ("reset password", Permission.ManageUsers, w => w.Accounts.ResetPassword(w.Keeper, "new")),
            ("change own password", Permission.Read, w => w.Accounts.ChangePassword("password", "new")),
            ("read audit", Permission.ReadAudit, w => w.Accounts.AuditLog(w.Clock.UtcNow, w.Clock.UtcNow)),
            ("balances", Permission.Read, w => w.Reports.Balances()),
            ("movements", Permission.Read, w => w.Reports.Movements(w.Clock.UtcNow, w.Clock.UtcNow)),
            ("turnover", Permission.Read, w => w.Reports.Turnover(w.Clock.UtcNow, w.Clock.UtcNow)),
            ("reconciliation", Permission.Read, w => w.Reports.Reconciliation(Guid.Empty)),
            ("export balances", Permission.Read, w => w.Reports.ExportBalances(ExportFormat.Excel)),
            ("export movements", Permission.Read, w => w.Reports.ExportMovements(ExportFormat.Pdf, w.Clock.UtcNow, w.Clock.UtcNow)),
            ("export turnover", Permission.Read, w => w.Reports.ExportTurnover(ExportFormat.Excel, w.Clock.UtcNow, w.Clock.UtcNow)),
            ("export inventory", Permission.Read, w => w.Reports.ExportReconciliation(ExportFormat.Pdf, Guid.Empty))
        };
        foreach (var (name, permission, action) in operations)
            foreach (var actor in new[] { "anonymous", "unknown", "locked", "disabled", "keeper", "auditor" })
            {
                var denied = actor is "anonymous" or "unknown" or "locked" or "disabled" ||
                    actor == "keeper" && permission is not (Permission.Read or Permission.ManageCatalog or Permission.ManageDocuments) ||
                    actor == "auditor" && permission is not (Permission.Read or Permission.Inventory or Permission.ReadAudit);
                if (denied) yield return [name, actor, (Action<object>)(value => action((World)value))];
            }
    }

    [Theory]
    [MemberData(nameof(ProtectedOperations))]
    public void Every_public_operation_rejects_unauthorized_identity_before_accessing_data(string name, string actor, Action<object> operation)
    {
        Assert.NotEmpty(name);
        var w = new World();
        var restricted = Guid.NewGuid();
        if (actor is "locked" or "disabled")
            w.Work.Seed(new UserAccount(restricted, "restricted", "h", Role.Administrator, actor == "locked" ? 5 : 0, actor != "disabled"));
        w.Identity.UserId = actor switch { "anonymous" => null, "unknown" => Guid.NewGuid(), "keeper" => w.Keeper, "auditor" => w.Auditor, _ => restricted };
        var commits = w.Work.Commits;
        Check.Error(ErrorCode.Forbidden, () => operation(w));
        Assert.Equal(commits, w.Work.Commits);
        Assert.Equal(0, w.Exporter.Calls);
    }

    [Fact]
    public void Keeper_and_auditor_can_complete_their_allowed_business_scenarios()
    {
        var w = new World();
        w.Identity.UserId = w.Keeper;
        w.Catalog.CreateProduct("R", "R", "R", "pcs");
        w.Catalog.SetMinimum(w.A.Id, w.P.Id, 1);
        w.Receipt(2, 3);
        w.Issue(1);
        Assert.Equal(1, Assert.Single(w.Reports.Balances()).Quantity);
        w.Identity.UserId = w.Auditor;
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        w.Inventories.Count(inventory.Id, w.P.Id, 0);
        w.Inventories.Complete(inventory.Id);
        Assert.Equal(0, Assert.Single(w.Reports.Balances()).Quantity);
        Assert.NotEmpty(w.Accounts.AuditLog(w.Clock.UtcNow, w.Clock.UtcNow));
    }
}
