namespace Warehouse.Tests;

public sealed class InventoryTests
{
    [Fact]
    public void Snapshot_counts_scanning_and_two_adjustment_acts_have_known_values()
    {
        var w = new World();
        w.Receipt(10, 4);
        w.Receipt(5, 3, productId: w.Q.Id);
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id, w.Q.Id]);
        Assert.Equal((10m, 40m), (inventory.Lines[0].BookQuantity, inventory.Lines[0].BookValue));
        Assert.Equal((5m, 15m), (inventory.Lines[1].BookQuantity, inventory.Lines[1].BookValue));
        w.Inventories.Count(inventory.Id, w.P.Id, 8);
        w.Inventories.Scan(inventory.Id, "00456", 5);
        var counted = w.Inventories.Scan(inventory.Id, " 00456 ", 2);
        Assert.Equal(7, counted.Lines[1].ActualQuantity);
        var completed = w.Inventories.Complete(inventory.Id);
        Assert.Equal(InventoryStatus.Completed, completed.Status);
        Assert.Equal(2, completed.AdjustmentDocumentIds.Count);
        Assert.Equal((8m, 32m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal((7m, 21m), (w.Stock(productId: w.Q.Id).Quantity, w.Stock(productId: w.Q.Id).Value));
        var acts = completed.AdjustmentDocumentIds.Select(w.Documents.GetDocument).ToArray();
        Assert.All(acts, x => { Assert.Equal(DocumentStatus.Posted, x.Status); Assert.Equal(inventory.Id, x.InventoryId); Assert.NotNull(x.Number); });
        Assert.Single(acts, x => x.Kind == DocumentKind.InventorySurplus);
        Assert.Single(acts, x => x.Kind == DocumentKind.InventoryShortage);
        Assert.Equal(-2, w.Inventories.GetInventory(inventory.Id).Lines[0].Difference);
        Assert.Single(w.Inventories.List());
        w.Issue(1); // Completion releases locks.
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Complete(inventory.Id));
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Cancel(inventory.Id));
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Count(inventory.Id, w.P.Id, 1));
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Scan(inventory.Id, "00123"));
    }

    [Fact]
    public void No_discrepancy_creates_no_documents_or_movements_but_zero_must_be_counted()
    {
        var w = new World();
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id, w.Q.Id]);
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Complete(inventory.Id));
        w.Inventories.Count(inventory.Id, w.P.Id, 0);
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Complete(inventory.Id));
        w.Inventories.Count(inventory.Id, w.Q.Id, 0);
        var complete = w.Inventories.Complete(inventory.Id);
        Assert.Empty(complete.AdjustmentDocumentIds);
        Assert.Empty(w.Read(s => s.Documents.All()));
        Assert.Empty(w.Read(s => s.Movements.All()));
        Assert.Equal(1, w.Receipt(1, 1).Number);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Single_direction_discrepancy_creates_only_its_act(bool surplus)
    {
        var w = new World();
        w.Receipt(2, 3);
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        w.Inventories.Count(inventory.Id, w.P.Id, surplus ? 4 : 0);
        var completed = w.Inventories.Complete(inventory.Id);
        var act = w.Documents.GetDocument(Assert.Single(completed.AdjustmentDocumentIds));
        Assert.Equal(surplus ? DocumentKind.InventorySurplus : DocumentKind.InventoryShortage, act.Kind);
        Assert.Equal(surplus ? 4 : 0, w.Stock().Quantity);
        Assert.Equal(surplus ? 12 : 0, w.Stock().Value);
        Check.Error(ErrorCode.Forbidden, () => w.Documents.Post(act.Id));
        w.Documents.Reverse(act.Id);
        Assert.Equal((2m, 6m), (w.Stock().Quantity, w.Stock().Value));
    }

    [Fact]
    public void Zero_book_surplus_requires_explicit_price_and_failure_keeps_inventory_active()
    {
        var w = new World();
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        w.Inventories.Count(inventory.Id, w.P.Id, 2);
        Check.Error(ErrorCode.InvalidValuation, () => w.Inventories.Complete(inventory.Id));
        Assert.Equal(InventoryStatus.Active, w.Inventories.GetInventory(inventory.Id).Status);
        Assert.Empty(w.Documents.List());
        Assert.Empty(w.Read(s => s.Movements.All()));
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 1, 1)]);
        Check.Error(ErrorCode.InventoryLocked, () => w.Documents.Post(draft.Id));
        w.Inventories.Count(inventory.Id, w.P.Id, 2, 7);
        w.Inventories.Complete(inventory.Id);
        Assert.Equal((2m, 14m), (w.Stock().Quantity, w.Stock().Value));
    }

    [Fact]
    public void Counting_can_be_corrected_and_scanner_retains_surplus_valuation()
    {
        var w = new World();
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        w.Inventories.Count(inventory.Id, w.P.Id, 10, 0);
        w.Inventories.Count(inventory.Id, w.P.Id, 0, 0);
        w.Inventories.Scan(inventory.Id, "00123", 0.5m);
        w.Inventories.Complete(inventory.Id);
        Assert.Equal((0.5m, 0m), (w.Stock().Quantity, w.Stock().Value));
    }

    [Fact]
    public void Active_inventory_blocks_all_movements_on_its_positions_in_both_transfer_directions()
    {
        var w = new World();
        var receipt = w.Receipt(10, 1);
        w.Receipt(10, 1, w.B.Id);
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        var drafts = new[]
        {
            w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 1, 1)]),
            w.Documents.Create(DocumentKind.Issue, w.A.Id, [new(w.P.Id, 1)]),
            w.Documents.Create(DocumentKind.Transfer, w.A.Id, [new(w.P.Id, 1)], w.B.Id),
            w.Documents.Create(DocumentKind.Transfer, w.B.Id, [new(w.P.Id, 1)], w.A.Id)
        };
        foreach (var document in drafts) Check.Error(ErrorCode.InventoryLocked, () => w.Documents.Post(document.Id));
        Check.Error(ErrorCode.InventoryLocked, () => w.Documents.Reverse(receipt.Id));
        Assert.Equal(10, w.Stock(w.B.Id).Quantity); // Target-lock failure rolls back source issue.
        Check.Error(ErrorCode.InventoryLocked, () => w.Inventories.Start(w.A.Id, [w.P.Id]));
        w.Receipt(1, 2, productId: w.Q.Id); // Other product is independent.
        w.Issue(1, w.B.Id); // Other warehouse is independent.
        var other = w.Inventories.Start(w.A.Id, [w.Q.Id]);
        w.Inventories.Cancel(other.Id);
        w.Inventories.Cancel(inventory.Id);
        Assert.Equal(InventoryStatus.Cancelled, w.Inventories.GetInventory(inventory.Id).Status);
        w.Documents.Post(drafts[0].Id);
        Assert.Equal(11, w.Stock().Quantity);
        w.Inventories.Start(w.A.Id, [w.P.Id]);
    }

    [Fact]
    public void Start_and_count_validation_reject_missing_archived_duplicates_and_outside_products()
    {
        var w = new World();
        var missing = Guid.NewGuid();
        Check.Error(ErrorCode.Validation, () => w.Inventories.Start(w.A.Id, []));
        Check.Error(ErrorCode.Validation, () => w.Inventories.Start(w.A.Id, null!));
        Check.Error(ErrorCode.Duplicate, () => w.Inventories.Start(w.A.Id, [w.P.Id, w.P.Id]));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Start(missing, [w.P.Id]));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Start(w.A.Id, [missing]));
        w.Catalog.SetProductActive(w.Q.Id, false);
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Start(w.A.Id, [w.Q.Id]));
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Count(inventory.Id, w.Q.Id, 0));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Scan(inventory.Id, w.Q.Barcode));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Scan(inventory.Id, "unknown"));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.GetInventory(missing));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Count(missing, w.P.Id, 0));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Scan(missing, w.P.Barcode));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Complete(missing));
        Check.Error(ErrorCode.NotFound, () => w.Inventories.Cancel(missing));
        Check.Error(ErrorCode.Validation, () => w.Inventories.Scan(inventory.Id, " "));
        Check.Error(ErrorCode.Validation, () => w.Inventories.Scan(inventory.Id, w.P.Barcode, -1));
        Check.Error(ErrorCode.Validation, () => w.Inventories.Count(inventory.Id, w.P.Id, -1));
        w.Catalog.UpdateWarehouse(w.B.Id, "B", false);
        Check.Error(ErrorCode.InvalidState, () => w.Inventories.Start(w.B.Id, [w.P.Id]));
        Assert.Single(w.Inventories.List());
    }

    [Fact]
    public void Storage_failure_on_completion_preserves_counts_snapshot_and_lock()
    {
        var w = new World();
        w.Receipt(2, 3);
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        w.Inventories.Count(inventory.Id, w.P.Id, 0);
        w.Work.FailCommit = true;
        Assert.Throws<IOException>(() => w.Inventories.Complete(inventory.Id));
        w.Work.FailCommit = false;
        Assert.Equal((2m, 6m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Single(w.Documents.List());
        var saved = w.Inventories.GetInventory(inventory.Id);
        Assert.Equal(InventoryStatus.Active, saved.Status);
        Assert.Equal(0, saved.Lines[0].ActualQuantity);
        Check.Error(ErrorCode.InventoryLocked, () => w.Issue(1));
        w.Inventories.Complete(inventory.Id);
        Assert.Equal(0, w.Stock().Quantity);
    }
}
