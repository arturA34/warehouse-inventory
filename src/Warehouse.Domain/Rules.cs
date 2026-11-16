namespace Warehouse.Domain;

public enum ErrorCode
{
    Validation, NotFound, Duplicate, Forbidden, InvalidState, InsufficientStock,
    InventoryLocked, Concurrency, InvalidCredentials, AccountLocked, InvalidValuation
}

public sealed class WarehouseException(ErrorCode code, string message) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}

public static class Rules
{
    public static void Require(bool condition, ErrorCode code, string message)
    {
        if (!condition) throw new WarehouseException(code, message);
    }

    public static Guid Id(Guid value)
    {
        Require(value != Guid.Empty, ErrorCode.Validation, "An identifier is required.");
        return value;
    }

    public static string Text(string? value)
    {
        Require(!string.IsNullOrWhiteSpace(value), ErrorCode.Validation, "A nonempty value is required.");
        return value!.Trim();
    }

    public static decimal Nonnegative(decimal value)
    {
        Require(value >= 0, ErrorCode.Validation, "The value cannot be negative.");
        return value;
    }

    public static decimal Positive(decimal value)
    {
        Require(value > 0, ErrorCode.Validation, "Quantity must be positive.");
        return value;
    }

    public static T Defined<T>(T value) where T : struct, Enum
    {
        Require(Enum.IsDefined(value), ErrorCode.Validation, "Unknown enum value.");
        return value;
    }

    public static decimal Calculate(Func<decimal> calculation)
    {
        try { return calculation(); }
        catch (OverflowException) { throw new WarehouseException(ErrorCode.Validation, "Decimal range exceeded."); }
    }
}
