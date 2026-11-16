namespace Warehouse.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void Drafts_do_not_change_stock_and_can_be_edited_until_posted()
    {
        var w = new World();
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, []);
        Assert.Null(draft.Number);
        Assert.Equal(0, w.Stock().Quantity);
        Assert.Empty(w.Read(s => s.Movements.All()));
        Check.Error(ErrorCode.Validation, () => w.Documents.Post(draft.Id));
        var edited = w.Documents.Edit(draft.Id, [new(w.P.Id, 2, 3)]);
        Assert.Single(edited.Lines);
        Assert.Equal(draft.Id, w.Documents.GetDocument(draft.Id).Id);
        Assert.Single(w.Documents.List());
        var posted = w.Documents.Post(draft.Id);
        Assert.Equal(1, posted.Number);
        Assert.Equal(2, w.Stock().Quantity);
        Assert.Equal(6, w.Stock().Value);
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Edit(draft.Id, []));
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Post(draft.Id));
        Assert.Single(w.Read(s => s.Movements.All()));
    }

    [Fact]
    public void Known_weighted_average_sequence_values_every_issue()
    {
        var w = new World();
        var one = w.Receipt(10, 10);
        var two = w.Receipt(30, 20);
        Assert.Equal(1, one.Number);
        Assert.Equal(2, two.Number);
        Assert.Equal((40m, 700m, 17.5m), (w.Stock().Quantity, w.Stock().Value, w.Stock().AverageCost));
        var issue = w.Issue(5);
        Assert.Equal((35m, 612.5m), (w.Stock().Quantity, w.Stock().Value));
        var movement = Assert.Single(w.Read(s => s.Movements.All()), x => x.DocumentId == issue.Id);
        Assert.Equal((-5m, -87.5m), (movement.Quantity, movement.Value));
        w.Issue(35);
        Assert.Equal((0m, 0m, 0m), (w.Stock().Quantity, w.Stock().Value, w.Stock().AverageCost));
        w.Receipt(1, 8);
        Assert.Equal(8, w.Stock().AverageCost);
    }

    [Fact]
    public void Free_goods_and_fractional_quantities_are_valid()
    {
        var w = new World();
        w.Receipt(0.5m, 0);
        w.Issue(0.25m);
        Assert.Equal((0.25m, 0m), (w.Stock().Quantity, w.Stock().Value));
        w.Issue(0.25m);
        Assert.Equal(0, w.Stock().Quantity);
    }

    [Fact]
    public void Transfer_preserves_total_quantity_and_value_and_recalculates_target_average()
    {
        var w = new World();
        w.Receipt(10, 10);
        w.Receipt(5, 20, w.B.Id);
        var transfer = w.Documents.Post(w.Documents.Create(DocumentKind.Transfer, w.A.Id, [new(w.P.Id, 5)], w.B.Id).Id);
        Assert.Equal((5m, 50m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal((10m, 150m, 15m), (w.Stock(w.B.Id).Quantity, w.Stock(w.B.Id).Value, w.Stock(w.B.Id).AverageCost));
        var movements = w.Read(s => s.Movements.All()).Where(x => x.DocumentId == transfer.Id).ToArray();
        Assert.Equal(2, movements.Length);
        Assert.Equal(0, movements.Sum(x => x.Quantity));
        Assert.Equal(0, movements.Sum(x => x.Value));
        w.Documents.Reverse(transfer.Id);
        Assert.Equal((10m, 100m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal((5m, 100m), (w.Stock(w.B.Id).Quantity, w.Stock(w.B.Id).Value));
    }

    [Theory]
    [InlineData(DocumentKind.Receipt)]
    [InlineData(DocumentKind.Issue)]
    [InlineData(DocumentKind.Transfer)]
    public void Reverse_restores_original_movements_and_retains_immutable_history(DocumentKind kind)
    {
        var w = new World();
        w.Receipt(10, 10);
        var document = kind == DocumentKind.Receipt ? w.Receipt(2, 20) : kind == DocumentKind.Issue ? w.Issue(2) :
            w.Documents.Post(w.Documents.Create(kind, w.A.Id, [new(w.P.Id, 2)], w.B.Id).Id);
        var originals = w.Read(s => s.Movements.All()).Where(x => x.DocumentId == document.Id).ToArray();
        var reversed = w.Documents.Reverse(document.Id);
        Assert.Equal(DocumentStatus.Reversed, reversed.Status);
        Assert.Equal(document.Number, reversed.Number);
        Assert.Equal((10m, 100m), (w.Stock().Quantity, w.Stock().Value));
        var history = w.Read(s => s.Movements.All()).Where(x => x.DocumentId == document.Id).ToArray();
        Assert.Equal(originals.Length * 2, history.Length);
        foreach (var original in originals)
        {
            Assert.Contains(original, history);
            var inverse = Assert.Single(history, x => x.ReversesMovementId == original.Id);
            Assert.Equal((-original.Quantity, -original.Value, original.Key), (inverse.Quantity, inverse.Value, inverse.Key));
        }
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Reverse(document.Id));
    }

    [Fact]
    public void Reversal_uses_original_value_after_later_receipts()
    {
        var w = new World();
        w.Receipt(10, 10);
        var issue = w.Issue(5);
        w.Receipt(5, 20);
        w.Documents.Reverse(issue.Id);
        Assert.Equal((15m, 200m), (w.Stock().Quantity, w.Stock().Value));
    }

    [Fact]
    public void Consumed_receipt_cannot_be_reversed_until_dependents_are_reversed()
    {
        var w = new World();
        var receipt = w.Receipt(10, 10);
        var issue = w.Issue(1);
        var movementCount = w.Read(s => s.Movements.All()).Count;
        Check.Error(ErrorCode.InsufficientStock, () => w.Documents.Reverse(receipt.Id));
        Assert.Equal(DocumentStatus.Posted, w.Documents.GetDocument(receipt.Id).Status);
        Assert.Equal(movementCount, w.Read(s => s.Movements.All()).Count);
        w.Documents.Reverse(issue.Id);
        w.Documents.Reverse(receipt.Id);
        Assert.Equal((0m, 0m), (w.Stock().Quantity, w.Stock().Value));
    }

    [Fact]
    public void Reversal_rejects_negative_or_orphaned_value_without_modifying_state()
    {
        var w = new World();
        var receipt = w.Receipt(1, 100);
        w.Issue(1);
        w.Receipt(2, 1);
        Check.Error(ErrorCode.InvalidValuation, () => w.Documents.Reverse(receipt.Id));
        Assert.Equal((2m, 2m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal(DocumentStatus.Posted, w.Documents.GetDocument(receipt.Id).Status);
    }

    [Theory]
    [InlineData(DocumentKind.Issue)]
    [InlineData(DocumentKind.Transfer)]
    public void Insufficient_stock_on_last_line_rolls_back_first_line_number_movements_and_audit(DocumentKind kind)
    {
        var w = new World();
        w.Receipt(5, 10);
        var draft = w.Documents.Create(kind, w.A.Id, [new(w.P.Id, 2), new(w.Q.Id, 1)], kind == DocumentKind.Transfer ? w.B.Id : null);
        var audits = w.Read(s => s.Audit.All()).Count;
        Check.Error(ErrorCode.InsufficientStock, () => w.Documents.Post(draft.Id));
        Assert.Equal((5m, 50m), (w.Stock().Quantity, w.Stock().Value));
        Assert.Equal(0, w.Stock(w.B.Id).Quantity);
        Assert.Equal(audits, w.Read(s => s.Audit.All()).Count);
        Assert.Single(w.Read(s => s.Movements.All()));
        Assert.Null(w.Documents.GetDocument(draft.Id).Number);
        Assert.Equal(2, w.Receipt(1, 1).Number);
    }

    [Fact]
    public void Failed_transfer_reversal_rolls_back_the_source_restoration()
    {
        var w = new World();
        w.Receipt(5, 10);
        var transfer = w.Documents.Post(w.Documents.Create(DocumentKind.Transfer, w.A.Id, [new(w.P.Id, 5)], w.B.Id).Id);
        w.Issue(1, w.B.Id);
        Check.Error(ErrorCode.InsufficientStock, () => w.Documents.Reverse(transfer.Id));
        Assert.Equal(0, w.Stock().Quantity);
        Assert.Equal(4, w.Stock(w.B.Id).Quantity);
        Assert.Equal(DocumentStatus.Posted, w.Documents.GetDocument(transfer.Id).Status);
        Assert.DoesNotContain(w.Read(s => s.Movements.All()), x => x.ReversesMovementId != null);
    }

    [Fact]
    public void Receipt_multiplication_overflow_is_atomic()
    {
        var w = new World();
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 1, 1), new(w.Q.Id, decimal.MaxValue, 2)]);
        Check.Error(ErrorCode.Validation, () => w.Documents.Post(draft.Id));
        Assert.Equal(0, w.Stock().Quantity);
        Assert.Empty(w.Read(s => s.Movements.All()));
    }

    [Fact]
    public void Missing_archived_and_inventory_only_document_inputs_are_rejected()
    {
        var w = new World();
        var missing = Guid.NewGuid();
        Check.Error(ErrorCode.NotFound, () => w.Documents.Create(DocumentKind.Receipt, missing, []));
        Check.Error(ErrorCode.NotFound, () => w.Documents.Create(DocumentKind.Transfer, w.A.Id, [], missing));
        Check.Error(ErrorCode.NotFound, () => w.Documents.Create(DocumentKind.Issue, w.A.Id, [new(missing, 1)]));
        Check.Error(ErrorCode.NotFound, () => w.Documents.GetDocument(missing));
        Check.Error(ErrorCode.NotFound, () => w.Documents.Edit(missing, []));
        Check.Error(ErrorCode.NotFound, () => w.Documents.Post(missing));
        Check.Error(ErrorCode.NotFound, () => w.Documents.Reverse(missing));
        Check.Error(ErrorCode.Forbidden, () => w.Documents.Create(DocumentKind.InventorySurplus, w.A.Id, []));
        Check.Error(ErrorCode.Forbidden, () => w.Documents.Create(DocumentKind.InventoryShortage, w.A.Id, []));
        var draft = w.Documents.Create(DocumentKind.Receipt, w.A.Id, [new(w.P.Id, 1, 1)]);
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Reverse(draft.Id));
        w.Catalog.SetProductActive(w.P.Id, false);
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Post(draft.Id));
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Edit(draft.Id, [new(w.P.Id, 1, 1)]));
        w.Catalog.SetProductActive(w.P.Id, true);
        w.Catalog.UpdateWarehouse(w.A.Id, "A", false);
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Post(draft.Id));
    }
}
