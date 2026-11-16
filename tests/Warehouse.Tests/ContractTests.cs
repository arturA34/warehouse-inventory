using System.Collections;
using System.Reflection;

namespace Warehouse.Tests;

public sealed class ContractTests
{
    [Fact]
    public void Domain_has_no_application_or_infrastructure_dependencies_and_application_only_references_domain()
    {
        var domain = typeof(Product).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(domain, x => x.Name!.StartsWith("Warehouse.", StringComparison.Ordinal));
        var application = typeof(CatalogService).Assembly.GetReferencedAssemblies();
        Assert.Equal(new[] { "Warehouse.Domain" }, application.Where(x => x.Name!.StartsWith("Warehouse.", StringComparison.Ordinal)).Select(x => x.Name));
        Assert.DoesNotContain(domain.Concat(application), x => x.Name!.Contains("EntityFramework", StringComparison.Ordinal) ||
            x.Name.Contains("Avalonia", StringComparison.Ordinal) || x.Name.Contains("Sqlite", StringComparison.Ordinal) || x.Name.Contains("Npgsql", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(StockMovement))]
    [InlineData(typeof(AuditEntry))]
    [InlineData(typeof(Product))]
    [InlineData(typeof(StockDocument))]
    [InlineData(typeof(InventorySession))]
    [InlineData(typeof(StockBalance))]
    public void Entities_do_not_expose_public_setters(Type type) =>
        Assert.DoesNotContain(type.GetProperties(), x => x.SetMethod?.IsPublic == true);

    [Theory]
    [InlineData(typeof(StockMovement))]
    [InlineData(typeof(AuditEntry))]
    public void Historical_repositories_expose_no_update_or_delete_operation(Type entityType)
    {
        var port = typeof(IAppendRepository<>).MakeGenericType(entityType);
        var methods = port.GetMethods().Concat(port.GetInterfaces().SelectMany(x => x.GetMethods())).Select(x => x.Name).Order().ToArray();
        Assert.Equal(new[] { "Add", "All", "Find" }, methods);
    }

    [Fact]
    public void Audit_retains_identity_target_action_and_timestamp()
    {
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var audit = new AuditEntry(id, actor, " Posted ", target, now);
        Assert.Equal((id, actor, target, "Posted", now), (audit.Id, audit.ActorId, audit.EntityId, audit.Action, audit.OccurredAt));
        var systemAudit = new AuditEntry(id, null, "Action", null, now);
        Assert.Null(systemAudit.ActorId);
        Assert.Null(systemAudit.EntityId);
        Assert.Equal(" hash ", new UserAccount(id, "login", " hash ", Role.Auditor).PasswordHash);
    }

    [Fact]
    public void Exposed_document_and_inventory_collections_cannot_be_modified()
    {
        var w = new World();
        var line = new DocumentLine(w.P.Id, 1, 1);
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [line]);
        Assert.Throws<NotSupportedException>(() => ((IList<DocumentLine>)draft.Lines)[0] = new(w.Q.Id, 5, 5));
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        Assert.Throws<NotSupportedException>(() => ((IList<InventoryLine>)inventory.Lines).Clear());
        w.Inventories.Count(inventory.Id, w.P.Id, 1, 2);
        var complete = w.Inventories.Complete(inventory.Id);
        Assert.Throws<NotSupportedException>(() => ((IList<Guid>)complete.AdjustmentDocumentIds).Clear());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("inventory")]
    public void Retried_operations_do_not_consume_single_pass_input_twice(string operation)
    {
        var w = new World();
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, []);
        var lines = new Once<DocumentLine>([new(w.P.Id, 1, 2)]);
        var ids = new Once<Guid>([w.P.Id]);
        w.Work.ConflictsRemaining = 2;
        if (operation == "create") Assert.Single(w.Documents.Create(DocumentKind.Receipt, w.A.Id, lines).Lines);
        else if (operation == "edit") Assert.Single(w.Documents.Edit(draft.Id, lines).Lines);
        else Assert.Single(w.Inventories.Start(w.A.Id, ids).Lines);
    }

    [Fact]
    public void Null_input_collections_produce_validation_errors_after_authorization()
    {
        var w = new World();
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, []);
        Check.Error(ErrorCode.Validation, () => w.Documents.Create(DocumentKind.Receipt, w.A.Id, null!));
        Check.Error(ErrorCode.Validation, () => w.Documents.Edit(draft.Id, null!));
        w.Identity.UserId = null;
        Check.Error(ErrorCode.Forbidden, () => w.Documents.Create(DocumentKind.Receipt, w.A.Id, new Once<DocumentLine>([], true)));
        Check.Error(ErrorCode.Forbidden, () => w.Inventories.Start(w.A.Id, new Once<Guid>([], true)));
    }

    private sealed class Once<T>(IEnumerable<T> items, bool failImmediately = false) : IEnumerable<T>
    {
        private bool read = failImmediately;
        public IEnumerator<T> GetEnumerator()
        {
            if (read) throw new InvalidOperationException("Input was enumerated more than once or before authorization.");
            read = true;
            return items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
