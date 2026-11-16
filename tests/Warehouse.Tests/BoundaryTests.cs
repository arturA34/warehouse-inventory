namespace Warehouse.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void Summation_is_exact_order_independent_and_checks_final_decimal_range()
    {
        var maximum = decimal.MaxValue;
        var minimumUnit = 0.0000000000000000000000000001m;
        Assert.Equal(maximum, Rules.Sum([maximum, maximum, -maximum]));
        Assert.Equal(-maximum, Rules.Sum([-maximum, -maximum, maximum]));
        Assert.Equal(minimumUnit, Rules.Sum([maximum, minimumUnit, -maximum]));
        Assert.Equal(0, Rules.Sum([]));
        Assert.Equal(-0.25m, Rules.Sum([1.5m, -1.75m]));
        Check.Error(ErrorCode.Validation, () => Rules.Sum([maximum, 1]));
        Check.Error(ErrorCode.Validation, () => Rules.Sum([-maximum, -1]));
        Check.Error(ErrorCode.Validation, () => Rules.Sum(null!));
    }

    [Fact]
    public void Turnover_is_correct_when_repository_returns_history_in_arbitrary_order()
    {
        var w = new World();
        var key = new StockKey(w.A.Id, w.P.Id);
        var first = w.Clock.UtcNow;
        // A valid history is receipt -> full issue -> receipt; repository order is not chronological.
        w.Work.Seed(new StockMovement(Guid.NewGuid(), Guid.NewGuid(), key, decimal.MaxValue, decimal.MaxValue, first.AddSeconds(2)));
        w.Work.Seed(new StockMovement(Guid.NewGuid(), Guid.NewGuid(), key, decimal.MaxValue, decimal.MaxValue, first));
        w.Work.Seed(new StockMovement(Guid.NewGuid(), Guid.NewGuid(), key, -decimal.MaxValue, -decimal.MaxValue, first.AddSeconds(1)));
        var row = Assert.Single(w.Reports.Turnover(first.AddSeconds(2), first.AddSeconds(2)));
        Assert.Equal(0, row.OpeningQuantity);
        Assert.Equal(0, row.OpeningValue);
        Assert.Equal(decimal.MaxValue, row.ReceivedQuantity);
        Assert.Equal(decimal.MaxValue, row.ReceivedValue);
        Assert.Equal(decimal.MaxValue, row.ClosingQuantity);
        Assert.Equal(decimal.MaxValue, row.ClosingValue);
        Check.Error(ErrorCode.Validation, () => w.Reports.Turnover(first, first.AddSeconds(2))); // Period receipts exceed decimal range.
    }

    [Fact]
    public void Archive_guards_distinguish_other_warehouses_and_other_products()
    {
        var w = new World();
        w.Receipt(1, 1, w.B.Id);
        var unrelated = w.Inventories.Start(w.B.Id, [w.P.Id]);
        Assert.False(w.Catalog.UpdateWarehouse(w.A.Id, "A", false).IsActive);
        Assert.False(w.Catalog.SetProductActive(w.Q.Id, false).IsActive);
        w.Catalog.UpdateWarehouse(w.A.Id, "A", true);
        w.Catalog.SetProductActive(w.Q.Id, true);
        var inventory = w.Inventories.Start(w.A.Id, [w.Q.Id]);
        Check.Error(ErrorCode.InventoryLocked, () => w.Catalog.UpdateWarehouse(w.A.Id, "A", false));
        w.Inventories.Cancel(inventory.Id);
        w.Inventories.Cancel(unrelated.Id);
    }

    [Fact]
    public void Maximum_threshold_and_empty_stock_trigger_alert_without_quantity_overflow()
    {
        var w = new World();
        w.Catalog.SetMinimum(w.A.Id, w.P.Id, decimal.MaxValue);
        Assert.Equal(decimal.MaxValue, Assert.Single(w.Reports.Balances(belowMinimumOnly: true)).Minimum);
        w.Receipt(decimal.MaxValue, 0);
        Assert.Empty(w.Reports.Balances(belowMinimumOnly: true));
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 1, 0)]);
        Check.Error(ErrorCode.Validation, () => w.Documents.Post(draft.Id));
        Assert.Equal(decimal.MaxValue, w.Stock().Quantity);
        w.Issue(decimal.MaxValue);
        Assert.Equal((0m, 0m), (w.Stock().Quantity, w.Stock().Value));
    }

    [Fact]
    public void Smallest_positive_quantity_survives_receipt_transfer_and_full_issue()
    {
        var w = new World();
        var tiny = 0.0000000000000000000000000001m;
        w.Receipt(tiny, 1);
        var transfer = w.Documents.Create(DocumentKind.Transfer, w.A.Id, [new(w.P.Id, tiny)], w.B.Id);
        w.Documents.Post(transfer.Id);
        Assert.Equal((tiny, tiny), (w.Stock(w.B.Id).Quantity, w.Stock(w.B.Id).Value));
        w.Issue(tiny, w.B.Id);
        Assert.Equal((0m, 0m), (w.Stock(w.B.Id).Quantity, w.Stock(w.B.Id).Value));
    }

    [Fact]
    public void A_posted_document_without_history_cannot_be_silently_reversed()
    {
        var w = new World();
        var document = StockDocument.Draft(Guid.NewGuid(), DocumentKind.Receipt, w.A.Id, null,
            [new(w.P.Id, 1, 2)], w.Clock.UtcNow).Post(7, w.Clock.UtcNow);
        w.Work.Seed(document); // Simulate incomplete/corrupted infrastructure state.
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Reverse(document.Id));
        Assert.Equal(DocumentStatus.Posted, w.Documents.GetDocument(document.Id).Status);
        Assert.Empty(w.Read(s => s.Movements.All()));
    }

    [Fact]
    public void Unicode_quotes_and_sql_like_catalog_values_remain_plain_data()
    {
        var w = new World();
        var product = w.Catalog.CreateProduct("' OR 1=1 --", "000'123", "Болт \"М8\" 🔩", "шт");
        Assert.Equal(product.Id, w.Catalog.FindByBarcode("000'123\r\n").Id);
        Assert.Equal(product.Id, Assert.Single(w.Catalog.Search("Болт").Items).Id);
        Assert.Equal(3, w.Catalog.Search().Total);
        Check.Error(ErrorCode.NotFound, () => w.Catalog.FindByBarcode("' OR 1=1 --"));
    }

    [Fact]
    public void Tiny_exported_quantities_keep_all_digits_without_trailing_zeros()
    {
        var w = new World();
        w.Receipt(0.0000000000000000000000000001m, 1);
        w.Reports.ExportBalances(ExportFormat.Excel);
        var row = Assert.Single(w.Exporter.LastReport!.Rows);
        Assert.Equal("0.0000000000000000000000000001", row[3]);
        Assert.Equal("0.0000000000000000000000000001", row[4]);
        Assert.Equal("1", row[5]);
    }

    [Fact]
    public void Two_act_inventory_completion_is_atomic_on_commit_failure()
    {
        var w = new World();
        w.Receipt(4, 3);
        w.Receipt(4, 5, productId: w.Q.Id);
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id, w.Q.Id]);
        w.Inventories.Count(inventory.Id, w.P.Id, 5);
        w.Inventories.Count(inventory.Id, w.Q.Id, 3);
        var audits = w.Read(s => s.Audit.All()).Count;
        w.Work.FailCommit = true;
        Assert.Throws<IOException>(() => w.Inventories.Complete(inventory.Id));
        w.Work.FailCommit = false;
        Assert.Equal((4m, 12m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal((4m, 20m), (w.Stock(productId: w.Q.Id).Quantity, w.Stock(productId: w.Q.Id).Value));
        Assert.Equal(2, w.Documents.List().Count);
        Assert.Equal(2, w.Read(s => s.Movements.All()).Count);
        Assert.Equal(audits, w.Read(s => s.Audit.All()).Count);
        Assert.Equal(InventoryStatus.Active, w.Inventories.GetInventory(inventory.Id).Status);
        var complete = w.Inventories.Complete(inventory.Id);
        Assert.Equal(new long?[] { 3, 4 }, complete.AdjustmentDocumentIds.Select(w.Documents.GetDocument).Select(x => x.Number));
        Assert.Equal((5m, 15m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal((3m, 15m), (w.Stock(productId: w.Q.Id).Quantity, w.Stock(productId: w.Q.Id).Value));
    }
}
