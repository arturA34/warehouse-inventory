namespace Warehouse.Domain;

public enum InventoryStatus { Active, Completed, Cancelled }

public sealed record InventoryLine
{
    public Guid ProductId { get; }
    public decimal BookQuantity { get; }
    public decimal BookValue { get; }
    public decimal? ActualQuantity { get; }
    public decimal? SurplusUnitCost { get; }
    public decimal? Difference { get; }

    public InventoryLine(Guid productId, decimal bookQuantity, decimal bookValue,
        decimal? actualQuantity = null, decimal? surplusUnitCost = null)
    {
        ProductId = Rules.Id(productId);
        BookQuantity = Rules.Nonnegative(bookQuantity);
        BookValue = Rules.Nonnegative(bookValue);
        Rules.Require(bookQuantity != 0 || bookValue == 0, ErrorCode.InvalidValuation, "Empty stock cannot retain value.");
        ActualQuantity = actualQuantity is { } actual ? Rules.Nonnegative(actual) : null;
        Difference = ActualQuantity is { } count ? Rules.Sum([count, -BookQuantity]) : null;
        SurplusUnitCost = surplusUnitCost is { } cost ? Rules.Nonnegative(cost) : null;
    }

    public InventoryLine Count(decimal actual, decimal? surplusUnitCost = null) =>
        new(ProductId, BookQuantity, BookValue, actual, surplusUnitCost);

    public decimal SurplusPrice()
    {
        Rules.Require(Difference > 0, ErrorCode.InvalidState, "The line has no surplus.");
        if (BookQuantity > 0) return Rules.Calculate(() => BookValue / BookQuantity);
        Rules.Require(SurplusUnitCost != null, ErrorCode.InvalidValuation, "Enter a valuation for surplus without book stock.");
        return SurplusUnitCost!.Value;
    }
}

public sealed record InventorySession
{
    public Guid Id { get; }
    public Guid WarehouseId { get; }
    public InventoryStatus Status { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; }
    public IReadOnlyList<InventoryLine> Lines { get; }
    public IReadOnlyList<Guid> AdjustmentDocumentIds { get; }

    private InventorySession(Guid id, Guid warehouseId, IEnumerable<InventoryLine> lines, DateTimeOffset startedAt,
        InventoryStatus status = InventoryStatus.Active, DateTimeOffset? finishedAt = null,
        IEnumerable<Guid>? adjustmentDocumentIds = null)
    {
        Id = Rules.Id(id);
        WarehouseId = Rules.Id(warehouseId);
        Rules.Require(lines != null, ErrorCode.Validation, "Inventory lines are required.");
        var copy = lines!.ToArray();
        Rules.Require(copy.Length > 0 && copy.All(x => x != null), ErrorCode.Validation, "Select at least one product.");
        Rules.Require(copy.Select(x => x.ProductId).Distinct().Count() == copy.Length, ErrorCode.Duplicate, "Duplicate inventory product.");
        Lines = Array.AsReadOnly(copy);
        StartedAt = startedAt;
        Status = status;
        FinishedAt = finishedAt;
        AdjustmentDocumentIds = Array.AsReadOnly((adjustmentDocumentIds ?? []).ToArray());
    }

    public static InventorySession Start(Guid id, Guid warehouseId, IEnumerable<InventoryLine> lines, DateTimeOffset now)
    {
        var result = new InventorySession(id, warehouseId, lines, now);
        Rules.Require(result.Lines.All(x => x.ActualQuantity == null), ErrorCode.Validation, "A new inventory must be uncounted.");
        return result;
    }

    public InventorySession Count(Guid productId, decimal actual, decimal? surplusUnitCost = null)
    {
        Active();
        Rules.Require(Lines.Any(x => x.ProductId == productId), ErrorCode.NotFound, "Product is outside this inventory.");
        return new(Id, WarehouseId, Lines.Select(x => x.ProductId == productId ? x.Count(actual, surplusUnitCost) : x), StartedAt);
    }

    public InventorySession Scan(Guid productId, decimal increment = 1)
    {
        Active();
        Rules.Positive(increment);
        var line = Lines.SingleOrDefault(x => x.ProductId == productId)
            ?? throw new WarehouseException(ErrorCode.NotFound, "Product is outside this inventory.");
        return Count(productId, Rules.Sum([line.ActualQuantity ?? 0, increment]), line.SurplusUnitCost);
    }

    public InventorySession Complete(IEnumerable<Guid> adjustmentDocumentIds, DateTimeOffset now)
    {
        Active();
        Rules.Require(Lines.All(x => x.ActualQuantity != null), ErrorCode.InvalidState, "Count every product, including zero quantities.");
        Rules.Require(now >= StartedAt, ErrorCode.Validation, "Completion cannot precede start.");
        Rules.Require(adjustmentDocumentIds != null, ErrorCode.Validation, "Adjustment identifiers are required.");
        var ids = adjustmentDocumentIds!.ToArray();
        foreach (var id in ids) Rules.Id(id);
        var expected = (Lines.Any(x => x.Difference > 0) ? 1 : 0) + (Lines.Any(x => x.Difference < 0) ? 1 : 0);
        Rules.Require(ids.Length == expected && ids.Distinct().Count() == ids.Length,
            ErrorCode.Validation, "Each discrepancy direction requires one adjustment act.");
        return new(Id, WarehouseId, Lines, StartedAt, InventoryStatus.Completed, now, ids);
    }

    public InventorySession Cancel(DateTimeOffset now)
    {
        Active();
        Rules.Require(now >= StartedAt, ErrorCode.Validation, "Cancellation cannot precede start.");
        return new(Id, WarehouseId, Lines, StartedAt, InventoryStatus.Cancelled, now);
    }

    private void Active() => Rules.Require(Status == InventoryStatus.Active, ErrorCode.InvalidState, "Inventory is closed.");
}
