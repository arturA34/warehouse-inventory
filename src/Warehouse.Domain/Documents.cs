namespace Warehouse.Domain;

public enum DocumentKind { Receipt, Issue, Transfer, InventorySurplus, InventoryShortage }
public enum DocumentStatus { Draft, Posted, Reversed }

public sealed record DocumentLine
{
    public Guid ProductId { get; }
    public decimal Quantity { get; }
    public decimal? UnitCost { get; }

    public DocumentLine(Guid productId, decimal quantity, decimal? unitCost = null)
    {
        ProductId = Rules.Id(productId);
        Quantity = Rules.Positive(quantity);
        UnitCost = unitCost is { } price ? Rules.Nonnegative(price) : null;
    }
}

public sealed record StockDocument
{
    public Guid Id { get; }
    public DocumentKind Kind { get; }
    public Guid WarehouseId { get; }
    public Guid? DestinationId { get; }
    public Guid? InventoryId { get; }
    public IReadOnlyList<DocumentLine> Lines { get; }
    public DocumentStatus Status { get; }
    public long? Number { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? PostedAt { get; }
    public DateTimeOffset? ReversedAt { get; }

    private StockDocument(Guid id, DocumentKind kind, Guid warehouseId, Guid? destinationId,
        Guid? inventoryId, IEnumerable<DocumentLine> lines, DateTimeOffset createdAt,
        DocumentStatus status = DocumentStatus.Draft, long? number = null,
        DateTimeOffset? postedAt = null, DateTimeOffset? reversedAt = null)
    {
        Id = Rules.Id(id);
        Kind = Rules.Defined(kind);
        WarehouseId = Rules.Id(warehouseId);
        DestinationId = destinationId is { } target ? Rules.Id(target) : null;
        InventoryId = inventoryId is { } inventory ? Rules.Id(inventory) : null;
        Rules.Require(kind == DocumentKind.Transfer ? destinationId != null && destinationId != warehouseId : destinationId == null,
            ErrorCode.Validation, "Transfers require two different warehouses; other documents use one warehouse.");
        Rules.Require((kind is DocumentKind.InventorySurplus or DocumentKind.InventoryShortage) == (inventoryId != null),
            ErrorCode.Validation, "Inventory acts must reference their inventory.");
        Rules.Require(lines != null, ErrorCode.Validation, "Lines are required.");
        var copy = lines!.ToArray();
        Rules.Require(copy.All(x => x != null), ErrorCode.Validation, "Null lines are not allowed.");
        Rules.Require(copy.Select(x => x.ProductId).Distinct().Count() == copy.Length, ErrorCode.Duplicate, "A product can appear only once.");
        foreach (var line in copy)
            Rules.Require(kind is DocumentKind.Receipt or DocumentKind.InventorySurplus ? line.UnitCost != null : line.UnitCost == null,
                ErrorCode.Validation, "Receipt lines require a price; outgoing lines use the stock average.");
        Lines = Array.AsReadOnly(copy);
        CreatedAt = createdAt;
        Status = status;
        Number = number;
        PostedAt = postedAt;
        ReversedAt = reversedAt;
    }

    public static StockDocument Draft(Guid id, DocumentKind kind, Guid warehouseId, Guid? destinationId,
        IEnumerable<DocumentLine> lines, DateTimeOffset now, Guid? inventoryId = null) =>
        new(id, kind, warehouseId, destinationId, inventoryId, lines, now);

    public StockDocument Edit(IEnumerable<DocumentLine> lines)
    {
        Rules.Require(Status == DocumentStatus.Draft, ErrorCode.InvalidState, "Only drafts can be edited.");
        return new(Id, Kind, WarehouseId, DestinationId, InventoryId, lines, CreatedAt);
    }

    public StockDocument Post(long number, DateTimeOffset now)
    {
        Rules.Require(Status == DocumentStatus.Draft, ErrorCode.InvalidState, "Only drafts can be posted.");
        Rules.Require(Lines.Count > 0 && number > 0 && now >= CreatedAt, ErrorCode.Validation, "Posting requires lines, a positive number and a valid time.");
        return new(Id, Kind, WarehouseId, DestinationId, InventoryId, Lines, CreatedAt, DocumentStatus.Posted, number, now);
    }

    public StockDocument Reverse(DateTimeOffset now)
    {
        Rules.Require(Status == DocumentStatus.Posted, ErrorCode.InvalidState, "Only posted documents can be reversed.");
        Rules.Require(now >= PostedAt, ErrorCode.Validation, "Reversal cannot precede posting.");
        return new(Id, Kind, WarehouseId, DestinationId, InventoryId, Lines, CreatedAt, DocumentStatus.Reversed, Number, PostedAt, now);
    }
}
