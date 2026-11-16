namespace Warehouse.Tests;

public sealed class TransactionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Conflicted_posting_retries_the_whole_transaction_without_duplicate_movements_or_numbers(int conflicts)
    {
        var w = new World();
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 2, 3)]);
        var audits = w.Read(s => s.Audit.All()).Count;
        var attempts = w.Work.Attempts;
        w.Work.ConflictsRemaining = conflicts;
        var posted = w.Documents.Post(draft.Id);
        Assert.Equal(attempts + conflicts + 1, w.Work.Attempts);
        Assert.Equal(1, posted.Number);
        Assert.Equal((2m, 6m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Single(w.Read(s => s.Movements.All()));
        Assert.Equal(audits + 1, w.Read(s => s.Audit.All()).Count);
    }

    [Fact]
    public void Three_conflicts_stop_retrying_and_leave_draft_unchanged()
    {
        var w = new World();
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 1, 1)]);
        var attempts = w.Work.Attempts;
        w.Work.ConflictsRemaining = 3;
        Check.Error(ErrorCode.Concurrency, () => w.Documents.Post(draft.Id));
        Assert.Equal(attempts + 3, w.Work.Attempts);
        Assert.Equal(DocumentStatus.Draft, w.Documents.GetDocument(draft.Id).Status);
        Assert.Empty(w.Read(s => s.Movements.All()));
        Assert.Equal(0, w.Stock().Quantity);
        Assert.Equal(1, w.Documents.Post(draft.Id).Number);
    }

    [Fact]
    public void Storage_failures_are_not_retried_and_roll_back_all_writes()
    {
        var w = new World();
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 1, 1)]);
        var attempts = w.Work.Attempts;
        w.Work.FailCommit = true;
        Assert.Throws<IOException>(() => w.Documents.Post(draft.Id));
        Assert.Equal(attempts + 1, w.Work.Attempts);
        w.Work.FailCommit = false;
        Assert.Null(w.Documents.GetDocument(draft.Id).Number);
        Assert.Empty(w.Read(s => s.Movements.All()));
        Assert.Equal(0, w.Stock().Quantity);
        Assert.Equal(1, w.Documents.Post(draft.Id).Number);
    }

    [Fact]
    public void Login_conflicts_count_one_failed_attempt_and_one_audit_entry()
    {
        var w = new World();
        var audits = w.Read(s => s.Audit.All()).Count;
        w.Work.ConflictsRemaining = 2;
        Check.Error(ErrorCode.InvalidCredentials, () => w.Accounts.SignIn("keeper", "wrong"));
        Assert.Equal(1, Assert.Single(w.Read(s => s.Users.All()), x => x.Id == w.Keeper).FailedAttempts);
        Assert.Equal(audits + 1, w.Read(s => s.Audit.All()).Count);
    }

    [Fact]
    public void Inventory_conflict_creates_each_act_and_audit_once()
    {
        var w = new World();
        w.Receipt(2, 3);
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        w.Inventories.Count(inventory.Id, w.P.Id, 0);
        w.Work.ConflictsRemaining = 2;
        var completed = w.Inventories.Complete(inventory.Id);
        var act = w.Documents.GetDocument(Assert.Single(completed.AdjustmentDocumentIds));
        Assert.Equal(2, act.Number);
        Assert.Equal(2, w.Documents.List().Count);
        Assert.Equal(2, w.Read(s => s.Movements.All()).Count);
        Assert.Equal(0, w.Stock().Quantity);
        Assert.Single(w.Read(s => s.Audit.All()), x => x.Action == "InventoryCompleted");
        Assert.Single(w.Read(s => s.Audit.All()), x => x.Action == "InventoryActPosted");
    }

    [Fact]
    public async Task Concurrent_issues_cannot_oversell()
    {
        var w = new World();
        w.Receipt(1, 10);
        var drafts = Enumerable.Range(0, 20).Select(_ => w.Documents.Create(DocumentKind.Issue, w.A.Id, [new(w.P.Id, 1)])).ToArray();
        var results = await Task.WhenAll(drafts.Select(draft => Task.Run(() =>
        {
            try { w.Documents.Post(draft.Id); return (ErrorCode?)null; }
            catch (WarehouseException error) { return error.Code; }
        })));
        Assert.Equal(1, results.Count(x => x == null));
        Assert.Equal(19, results.Count(x => x == ErrorCode.InsufficientStock));
        Assert.Equal((0m, 0m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal(2, w.Read(s => s.Movements.All()).Count);
        Assert.Equal(2, w.Documents.List().Count(x => x.Status == DocumentStatus.Posted));
    }

    [Fact]
    public async Task Concurrent_inventory_starts_have_exactly_one_owner_even_for_absent_stock()
    {
        var w = new World();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try { w.Inventories.Start(w.A.Id, [w.P.Id]); return (ErrorCode?)null; }
            catch (WarehouseException error) { return error.Code; }
        })));
        Assert.Equal(1, results.Count(x => x == null));
        Assert.Equal(19, results.Count(x => x == ErrorCode.InventoryLocked));
        Assert.Single(w.Inventories.List());
    }

    [Fact]
    public async Task Concurrent_login_failures_stop_at_exactly_five()
    {
        var w = new World();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try { w.Accounts.SignIn("keeper", "bad"); return (ErrorCode?)null; }
            catch (WarehouseException error) { return error.Code; }
        })));
        Assert.Equal(4, results.Count(x => x == ErrorCode.InvalidCredentials));
        Assert.Equal(16, results.Count(x => x == ErrorCode.AccountLocked));
        Assert.Equal(5, Assert.Single(w.Read(s => s.Users.All()), x => x.Id == w.Keeper).FailedAttempts);
    }
}
