namespace Warehouse.Tests;

internal static class Check
{
    public static void Error(ErrorCode expected, Action action) => Assert.Equal(expected, Assert.Throws<WarehouseException>(action).Code);
}
