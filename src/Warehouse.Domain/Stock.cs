namespace Warehouse.Domain;

public sealed record StockBalance
{
    public StockKey Key { get; }
    public decimal Quantity { get; }
    public decimal Value { get; }
    public decimal AverageCost => Quantity == 0 ? 0 : Rules.Calculate(() => Value / Quantity);

    public StockBalance(StockKey key, decimal quantity = 0, decimal value = 0)
    {
        key.Validate();
        Key = key;
        Quantity = Rules.Nonnegative(quantity);
        Value = Rules.Nonnegative(value);
        Rules.Require(quantity != 0 || value == 0, ErrorCode.InvalidValuation, "Empty stock cannot retain value.");
    }

    public decimal IssueValue(decimal quantity)
    {
        Rules.Positive(quantity);
        Rules.Require(quantity <= Quantity, ErrorCode.InsufficientStock, "Not enough stock.");
        return quantity == Quantity ? Value : DecimalMath.Proportion(Value, quantity, Quantity);
    }

    public StockBalance Apply(decimal quantity, decimal value)
    {
        Rules.Require(quantity != 0, ErrorCode.Validation, "A movement must change quantity.");
        Rules.Require(value == 0 || Math.Sign(value) == Math.Sign(quantity), ErrorCode.InvalidValuation, "Quantity and value must have the same sign.");
        var nextQuantity = Rules.Calculate(() => Quantity + quantity);
        var nextValue = Rules.Calculate(() => Value + value);
        Rules.Require(nextQuantity >= 0, ErrorCode.InsufficientStock, "Not enough stock.");
        Rules.Require(nextValue >= 0 && (nextQuantity != 0 || nextValue == 0), ErrorCode.InvalidValuation,
            "Reversal is incompatible with subsequent valuation. Reverse dependent documents first.");
        return new StockBalance(Key, nextQuantity, nextValue);
    }
}

public sealed record StockMovement
{
    public Guid Id { get; }
    public Guid DocumentId { get; }
    public StockKey Key { get; }
    public decimal Quantity { get; }
    public decimal Value { get; }
    public DateTimeOffset OccurredAt { get; }
    public Guid? ReversesMovementId { get; }

    public StockMovement(Guid id, Guid documentId, StockKey key, decimal quantity, decimal value,
        DateTimeOffset occurredAt, Guid? reversesMovementId = null)
    {
        Id = Rules.Id(id);
        DocumentId = Rules.Id(documentId);
        key.Validate();
        Key = key;
        Rules.Require(quantity != 0, ErrorCode.Validation, "A movement must change quantity.");
        Rules.Require(value == 0 || Math.Sign(value) == Math.Sign(quantity), ErrorCode.InvalidValuation, "Invalid movement valuation.");
        Quantity = quantity;
        Value = value;
        OccurredAt = occurredAt;
        ReversesMovementId = reversesMovementId is { } source ? Rules.Id(source) : null;
        Rules.Require(reversesMovementId != id, ErrorCode.Validation, "A movement cannot reverse itself.");
    }
}
