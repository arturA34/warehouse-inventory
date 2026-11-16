namespace Warehouse.Tests;

public sealed class InventoryDomainTests
{
    private static readonly Guid P = Guid.NewGuid();
    private static readonly Guid Q = Guid.NewGuid();
    private static readonly Guid W = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static InventorySession Start(params InventoryLine[] lines) => InventorySession.Start(Guid.NewGuid(), W, lines, Now);

    [Fact]
    public void Start_requires_valid_identifiers_nonempty_unique_uncounted_lines()
    {
        Check.Error(ErrorCode.Validation, () => Start());
        Check.Error(ErrorCode.Validation, () => Start(null!));
        Check.Error(ErrorCode.Validation, () => Start([null!]));
        Check.Error(ErrorCode.Validation, () => Start(new InventoryLine(P, 0, 0, 0)));
        Check.Error(ErrorCode.Duplicate, () => Start(new(P, 0, 0), new(P, 1, 1)));
        Check.Error(ErrorCode.Validation, () => InventorySession.Start(Guid.Empty, W, [new(P, 0, 0)], Now));
        Check.Error(ErrorCode.Validation, () => InventorySession.Start(P, Guid.Empty, [new(P, 0, 0)], Now));
    }

    [Fact]
    public void Counting_and_scanning_are_distinct_from_uncounted_zero()
    {
        var inventory = Start(new(P, 5, 10), new(Q, 0, 0));
        Assert.Null(inventory.Lines[0].Difference);
        var counted = inventory.Count(P, 0).Scan(Q).Scan(Q, 0.5m);
        Assert.Equal(-5, counted.Lines[0].Difference);
        Assert.Equal(1.5m, counted.Lines[1].Difference);
        Assert.Null(inventory.Lines[0].ActualQuantity);
        Assert.Equal(2, counted.Count(Q, 2, 3).Lines[1].ActualQuantity);
        Check.Error(ErrorCode.NotFound, () => inventory.Count(Guid.NewGuid(), 1));
        Check.Error(ErrorCode.NotFound, () => inventory.Scan(Guid.NewGuid()));
        Check.Error(ErrorCode.Validation, () => inventory.Scan(P, 0));
        Check.Error(ErrorCode.Validation, () => inventory.Scan(P, -1));
        Check.Error(ErrorCode.Validation, () => inventory.Count(P, -1));
        Check.Error(ErrorCode.Validation, () => inventory.Count(P, decimal.MaxValue).Scan(P));
    }

    [Fact]
    public void Completion_requires_all_counts_and_one_act_per_direction()
    {
        var inventory = Start(new(P, 5, 10), new(Q, 0, 0));
        Check.Error(ErrorCode.InvalidState, () => inventory.Complete([], Now));
        Check.Error(ErrorCode.InvalidState, () => inventory.Count(P, 5).Complete([], Now));
        var counted = inventory.Count(P, 0).Count(Q, 2, 3);
        Check.Error(ErrorCode.Validation, () => counted.Complete([], Now));
        Check.Error(ErrorCode.Validation, () => counted.Complete([P], Now));
        Check.Error(ErrorCode.Validation, () => counted.Complete([P, P], Now));
        Check.Error(ErrorCode.Validation, () => counted.Complete([Guid.Empty, Q], Now));
        Check.Error(ErrorCode.Validation, () => counted.Complete(null!, Now));
        Check.Error(ErrorCode.Validation, () => counted.Complete([P, Q], Now.AddTicks(-1)));
        var completed = counted.Complete([P, Q], Now);
        Assert.Equal(InventoryStatus.Completed, completed.Status);
        Assert.Equal(Now, completed.FinishedAt);
        Assert.Equal(new[] { P, Q }, completed.AdjustmentDocumentIds);
        var noDifference = inventory.Count(P, 5).Count(Q, 0).Complete([], Now);
        Assert.Empty(noDifference.AdjustmentDocumentIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Closed_inventories_reject_every_mutation(bool completed)
    {
        var inventory = Start(new InventoryLine(P, 0, 0)).Count(P, 0);
        inventory = completed ? inventory.Complete([], Now) : inventory.Cancel(Now);
        Check.Error(ErrorCode.InvalidState, () => inventory.Count(P, 0));
        Check.Error(ErrorCode.InvalidState, () => inventory.Scan(P));
        Check.Error(ErrorCode.InvalidState, () => inventory.Cancel(Now));
        Check.Error(ErrorCode.InvalidState, () => inventory.Complete([], Now));
        Assert.Equal(completed ? InventoryStatus.Completed : InventoryStatus.Cancelled, inventory.Status);
        Check.Error(ErrorCode.Validation, () => Start(new InventoryLine(P, 0, 0)).Cancel(Now.AddTicks(-1)));
    }

    [Fact]
    public void Surplus_uses_frozen_book_cost_or_explicit_price_for_zero_book_stock()
    {
        Assert.Equal(2, new InventoryLine(P, 5, 10, 7, 999).SurplusPrice());
        Assert.Equal(3, new InventoryLine(P, 0, 0, 2, 3).SurplusPrice());
        Assert.Equal(0, new InventoryLine(P, 0, 0, 2, 0).SurplusPrice());
        Check.Error(ErrorCode.InvalidValuation, () => new InventoryLine(P, 0, 0, 1).SurplusPrice());
        Check.Error(ErrorCode.InvalidState, () => new InventoryLine(P, 1, 2).SurplusPrice());
        Check.Error(ErrorCode.InvalidState, () => new InventoryLine(P, 1, 2, 0).SurplusPrice());
        Check.Error(ErrorCode.InvalidState, () => new InventoryLine(P, 1, 2, 1).SurplusPrice());
        Check.Error(ErrorCode.Validation, () => new InventoryLine(P, 0.0000000000000000000000000001m, decimal.MaxValue, 1).SurplusPrice());
    }
}
