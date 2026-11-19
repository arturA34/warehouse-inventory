namespace Warehouse.Tests;

public sealed class DomainTests
{
    private static readonly Guid ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly StockKey Key = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), ProductId);
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void Required_text_rejects_empty_inputs(string? text)
    {
        Check.Error(ErrorCode.Validation, () => Rules.Text(text));
        Check.Error(ErrorCode.Validation, () => new Product(ProductId, text!, "code", "name", "unit"));
        Check.Error(ErrorCode.Validation, () => new Product(ProductId, "sku", text!, "name", "unit"));
        Check.Error(ErrorCode.Validation, () => new Product(ProductId, "sku", "code", text!, "unit"));
        Check.Error(ErrorCode.Validation, () => new Product(ProductId, "sku", "code", "name", text!));
        Check.Error(ErrorCode.Validation, () => new WarehouseSite(Key.WarehouseId, text!));
        Check.Error(ErrorCode.Validation, () => new UserAccount(ProductId, text!, "hash", Role.Auditor));
        Check.Error(ErrorCode.Validation, () => new UserAccount(ProductId, "login", text!, Role.Auditor));
        Check.Error(ErrorCode.Validation, () => new AuditEntry(ProductId, null, text!, null, Now));
    }

    [Fact]
    public void Text_is_trimmed_and_barcode_leading_zeros_are_preserved()
    {
        var product = new Product(ProductId, " a ", " 001 ", " Товар ", " кг ");
        Assert.Equal(("a", "001", "Товар", "кг"), (product.Sku, product.Barcode, product.Name, product.Unit));
        Assert.Equal("Main", new WarehouseSite(Key.WarehouseId, " Main ").Name);
        Assert.Equal("user", new UserAccount(ProductId, " user ", "hash", Role.Auditor).Login);
    }

    [Fact]
    public void Empty_identifiers_are_invalid_in_all_entities()
    {
        var empty = Guid.Empty;
        Check.Error(ErrorCode.Validation, () => new Product(empty, "s", "b", "n", "u"));
        Check.Error(ErrorCode.Validation, () => new WarehouseSite(empty, "n"));
        Check.Error(ErrorCode.Validation, () => new StockKey(empty, ProductId));
        Check.Error(ErrorCode.Validation, () => new StockKey(Key.WarehouseId, empty));
        Check.Error(ErrorCode.Validation, () => new StockBalance(default));
        Check.Error(ErrorCode.Validation, () => new DocumentLine(empty, 1));
        Check.Error(ErrorCode.Validation, () => new MinimumStock(empty, Key, 0));
        Check.Error(ErrorCode.Validation, () => new MinimumStock(ProductId, default, 0));
        Check.Error(ErrorCode.Validation, () => new UserAccount(empty, "l", "h", Role.Auditor));
        Check.Error(ErrorCode.Validation, () => new AuditEntry(empty, null, "a", null, Now));
        Check.Error(ErrorCode.Validation, () => new AuditEntry(ProductId, empty, "a", null, Now));
        Check.Error(ErrorCode.Validation, () => new AuditEntry(ProductId, null, "a", empty, Now));
        Check.Error(ErrorCode.Validation, () => new InventoryLine(empty, 0, 0));
        Check.Error(ErrorCode.Validation, () => new StockMovement(empty, ProductId, Key, 1, 0, Now));
        Check.Error(ErrorCode.Validation, () => new StockMovement(ProductId, empty, Key, 1, 0, Now));
        Check.Error(ErrorCode.Validation, () => new StockMovement(ProductId, ProductId, default, 1, 0, Now));
        Check.Error(ErrorCode.Validation, () => new StockMovement(ProductId, ProductId, Key, 1, 0, Now, empty));
        Check.Error(ErrorCode.Validation, () => new StockMovement(ProductId, ProductId, Key, 1, 0, Now, ProductId));
    }

    [Theory]
    [InlineData("-0.0000000000000000000000000001")]
    [InlineData("-1")]
    [InlineData("-79228162514264337593543950335")]
    public void Negative_quantities_prices_and_thresholds_are_rejected(string input)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Check.Error(ErrorCode.Validation, () => new DocumentLine(ProductId, value));
        Check.Error(ErrorCode.Validation, () => new DocumentLine(ProductId, 1, value));
        Check.Error(ErrorCode.Validation, () => new StockBalance(Key, value));
        Check.Error(ErrorCode.Validation, () => new StockBalance(Key, 1, value));
        Check.Error(ErrorCode.Validation, () => new MinimumStock(ProductId, Key, value));
        Check.Error(ErrorCode.Validation, () => new InventoryLine(ProductId, value, 0));
        Check.Error(ErrorCode.Validation, () => new InventoryLine(ProductId, 1, value));
        Check.Error(ErrorCode.Validation, () => new InventoryLine(ProductId, 1, 0, value));
        Check.Error(ErrorCode.Validation, () => new InventoryLine(ProductId, 1, 0, 0, value));
    }

    [Fact]
    public void Zero_is_valid_for_prices_counts_and_minimum_but_not_document_quantity()
    {
        Check.Error(ErrorCode.Validation, () => new DocumentLine(ProductId, 0));
        Assert.Equal(0, new DocumentLine(ProductId, 1, 0).UnitCost);
        Assert.Equal(0, new MinimumStock(ProductId, Key, 0).Quantity);
        Assert.Equal(0, new InventoryLine(ProductId, 0, 0, 0).Difference);
        Assert.Equal(0, new StockBalance(Key).AverageCost);
        Check.Error(ErrorCode.InvalidValuation, () => new StockBalance(Key, 0, 1));
        Check.Error(ErrorCode.InvalidValuation, () => new InventoryLine(ProductId, 0, 1));
    }

    [Fact]
    public void Weighted_cost_and_partial_and_full_issue_have_known_expected_values()
    {
        var stock = new StockBalance(Key).Apply(10, 100).Apply(30, 600);
        Assert.Equal(40, stock.Quantity);
        Assert.Equal(700, stock.Value);
        Assert.Equal(17.5m, stock.AverageCost);
        Assert.Equal(87.5m, stock.IssueValue(5));
        var final = stock.Apply(-5, -87.5m);
        Assert.Equal(35, final.Quantity);
        Assert.Equal(612.5m, final.Value);
        Assert.Equal(612.5m, final.IssueValue(35));
        Assert.Equal(new StockBalance(Key), final.Apply(-35, -612.5m));
        Assert.Equal(700, stock.Value); // Исходный остаток сохранился.
    }

    [Fact]
    public void Repeating_decimal_average_does_not_leave_value_after_full_depletion()
    {
        var stock = new StockBalance(Key, 3, 1);
        var first = stock.IssueValue(1);
        Assert.InRange(first, 0.3333333333333333333333333333m, 0.3333333333333333333333333334m);
        stock = stock.Apply(-1, -first);
        var remaining = stock.IssueValue(2);
        Assert.Equal(1, first + remaining);
        Assert.Equal(0, stock.Apply(-2, -remaining).Value);
    }

    [Fact]
    public void Stock_boundary_failures_are_explicit()
    {
        var stock = new StockBalance(Key, 1, 2);
        Check.Error(ErrorCode.Validation, () => stock.IssueValue(0));
        Check.Error(ErrorCode.Validation, () => stock.IssueValue(-1));
        Check.Error(ErrorCode.InsufficientStock, () => stock.IssueValue(1.0000000000000000000000000001m));
        Check.Error(ErrorCode.InsufficientStock, () => new StockBalance(Key).IssueValue(0.0000000000000000000000000001m));
        Check.Error(ErrorCode.InsufficientStock, () => stock.Apply(-2, -2));
        Check.Error(ErrorCode.InvalidValuation, () => stock.Apply(-1, -3));
        Check.Error(ErrorCode.InvalidValuation, () => stock.Apply(-1, -1));
        Check.Error(ErrorCode.InvalidValuation, () => stock.Apply(1, -1));
        Check.Error(ErrorCode.InvalidValuation, () => stock.Apply(-1, 1));
        Check.Error(ErrorCode.Validation, () => stock.Apply(0, 0));
        Assert.Equal(0, new StockBalance(Key, 1, 0).IssueValue(1));
    }

    [Fact]
    public void Decimal_limits_and_overflow_are_handled_without_wrapping()
    {
        var tiny = 0.0000000000000000000000000001m;
        Assert.Equal(tiny, new DocumentLine(ProductId, tiny).Quantity);
        Assert.Equal(decimal.MaxValue, new StockBalance(Key, decimal.MaxValue, decimal.MaxValue).IssueValue(decimal.MaxValue));
        Check.Error(ErrorCode.Validation, () => new StockBalance(Key, decimal.MaxValue).Apply(1, 0));
        Check.Error(ErrorCode.Validation, () => new StockBalance(Key, 1, decimal.MaxValue).Apply(1, 1));
        Check.Error(ErrorCode.Validation, () => _ = new StockBalance(Key, tiny, decimal.MaxValue).AverageCost);
    }

    [Fact]
    public void Proportional_value_has_no_intermediate_overflow_or_repeated_rounding()
    {
        var tiny = 0.0000000000000000000000000001m;
        Assert.Equal(2, new StockBalance(Key, 0.75m, 6).IssueValue(0.25m));
        Assert.Equal(1, new StockBalance(Key, decimal.MaxValue, decimal.MaxValue).IssueValue(1));
        Assert.Equal(5, new StockBalance(Key, 2 * tiny, 10).IssueValue(tiny));
        Assert.Equal(0, new StockBalance(Key, 2, tiny).IssueValue(1)); // Ровно половина минимального шага округляется к чётному.
        Assert.Equal(2 * tiny, new StockBalance(Key, 2, 3 * tiny).IssueValue(1));
        Assert.Equal(tiny, new StockBalance(Key, 3, 2 * tiny).IssueValue(1));
        Assert.Equal(decimal.MaxValue - 1, new StockBalance(Key, decimal.MaxValue, decimal.MaxValue).IssueValue(decimal.MaxValue - 1));
        Assert.Equal(0, new StockBalance(Key, 3, 0).IssueValue(1));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    [InlineData(1, true)]
    public void Movement_signs_and_zero_are_validated(int quantity, bool negativeValue)
    {
        var value = negativeValue ? -2 : 2;
        if (quantity == 0) Check.Error(ErrorCode.Validation, () => new StockMovement(ProductId, ProductId, Key, quantity, value, Now));
        else if (Math.Sign(quantity) != Math.Sign(value)) Check.Error(ErrorCode.InvalidValuation, () => new StockMovement(ProductId, ProductId, Key, quantity, value, Now));
        else Assert.Equal(value, new StockMovement(ProductId, ProductId, Key, quantity, value, Now).Value);
    }

    [Fact]
    public void Document_lifecycle_is_one_way_with_number_assigned_only_on_posting()
    {
        var lines = new List<DocumentLine> { new(ProductId, 2, 3) };
        var draft = StockDocument.Draft(ProductId, DocumentKind.Receipt, Key.WarehouseId, null, lines, Now);
        lines.Clear();
        Assert.Single(draft.Lines);
        Assert.Null(draft.Number);
        Assert.Null(draft.PostedAt);
        Assert.Null(draft.ReversedAt);
        Assert.Empty(draft.Edit([]).Lines);
        var posted = draft.Post(1, Now);
        Assert.Equal(DocumentStatus.Draft, draft.Status);
        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal(1, posted.Number);
        Check.Error(ErrorCode.InvalidState, () => posted.Edit([]));
        Check.Error(ErrorCode.InvalidState, () => posted.Post(2, Now));
        Check.Error(ErrorCode.InvalidState, () => draft.Reverse(Now));
        var reversed = posted.Reverse(Now);
        Assert.Equal(DocumentStatus.Reversed, reversed.Status);
        Assert.Equal(1, reversed.Number);
        Assert.Equal(Now, reversed.ReversedAt);
        Check.Error(ErrorCode.InvalidState, () => reversed.Edit([]));
        Check.Error(ErrorCode.InvalidState, () => reversed.Post(2, Now));
        Check.Error(ErrorCode.InvalidState, () => reversed.Reverse(Now));
    }

    [Fact]
    public void Document_header_and_line_validation_covers_invalid_combinations()
    {
        StockDocument Draft(DocumentKind kind, Guid? destination = null, Guid? inventory = null, IEnumerable<DocumentLine>? lines = null) =>
            StockDocument.Draft(ProductId, kind, Key.WarehouseId, destination, lines ?? [new(ProductId, 1)], Now, inventory);
        Check.Error(ErrorCode.Validation, () => Draft((DocumentKind)99));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Transfer));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Transfer, Key.WarehouseId));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Transfer, Guid.Empty));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Issue, ProductId));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.InventoryShortage));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Issue, inventory: ProductId));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.InventoryShortage, inventory: Guid.Empty));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Receipt));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Issue, lines: [new(ProductId, 1, 0)]));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Transfer, ProductId, lines: [new(ProductId, 1, 1)]));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.InventorySurplus, inventory: ProductId));
        Check.Error(ErrorCode.Duplicate, () => Draft(DocumentKind.Issue, lines: [new(ProductId, 1), new(ProductId, 2)]));
        Check.Error(ErrorCode.Validation, () => Draft(DocumentKind.Issue, lines: [null!]));
        Check.Error(ErrorCode.Validation, () => StockDocument.Draft(ProductId, DocumentKind.Issue, Key.WarehouseId, null, null!, Now));
        Check.Error(ErrorCode.Validation, () => StockDocument.Draft(Guid.Empty, DocumentKind.Issue, Key.WarehouseId, null, [], Now));
        Check.Error(ErrorCode.Validation, () => StockDocument.Draft(ProductId, DocumentKind.Issue, Guid.Empty, null, [], Now));
        Assert.Equal(DocumentKind.Transfer, Draft(DocumentKind.Transfer, ProductId).Kind);
        Assert.Equal(ProductId, Draft(DocumentKind.InventoryShortage, inventory: ProductId).InventoryId);
    }

    [Fact]
    public void Posting_rejects_empty_lines_nonpositive_number_and_time_travel()
    {
        var draft = StockDocument.Draft(ProductId, DocumentKind.Issue, Key.WarehouseId, null, [new(ProductId, 1)], Now);
        Check.Error(ErrorCode.Validation, () => draft.Edit([]).Post(1, Now));
        Check.Error(ErrorCode.Validation, () => draft.Post(0, Now));
        Check.Error(ErrorCode.Validation, () => draft.Post(-1, Now));
        Check.Error(ErrorCode.Validation, () => draft.Post(1, Now.AddTicks(-1)));
        Check.Error(ErrorCode.Validation, () => draft.Post(1, Now).Reverse(Now.AddTicks(-1)));
        Assert.Equal(long.MaxValue, draft.Post(long.MaxValue, Now).Number);
    }
}
