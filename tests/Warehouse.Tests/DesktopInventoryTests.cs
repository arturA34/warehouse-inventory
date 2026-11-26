using Warehouse.Desktop.ViewModels;
using Warehouse.Domain;

namespace Warehouse.Tests;

public sealed class DesktopInventoryTests
{
    [Fact]
    public void ScanPreservesLeadingZeroAndAccumulatesWhileManualZeroIsDistinct()
    {
        var world = new World();
        world.Receipt(3m, 4m);
        world.Receipt(2m, 1m, productId: world.Q.Id);
        var opened = Guid.Empty;
        var model = Open(world, id => opened = id);
        model.Warehouse = world.A;
        model.Planned.Add(world.P);
        model.Planned.Add(world.Q);
        model.Start.Execute(null);
        Assert.All(model.Rows, row => Assert.Equal("Не пересчитано", row.Actual));

        model.Complete.Execute(null);
        Assert.Contains("Пересчитайте все позиции", model.Error);
        foreach (var barcode in new[] { "00123", "00123" })
        {
            model.Barcode = barcode;
            model.Scan.Execute(null);
            Assert.Equal("", model.Barcode);
        }
        Assert.Equal(2m, model.CurrentRow?.Line.ActualQuantity);
        model.CurrentRow = model.Rows.Single(x => x.Product.Id == world.Q.Id);
        model.Actual = 0;
        model.SaveCount.Execute(null);
        Assert.Equal("0", model.CurrentRow?.Actual);

        model.Complete.Execute(null);
        Assert.False(model.CanEdit);
        Assert.Equal(InventoryStatus.Completed, model.Selected?.Inventory.Status);
        var act = Assert.Single(model.Acts);
        model.OpenAct.Execute(null);
        Assert.Equal(act.Id, opened);
        Assert.Equal(DocumentKind.InventoryShortage, world.Documents.GetDocument(opened).Kind);
        Assert.Equal(2m, world.Stock().Quantity);
        Assert.Equal(0m, world.Stock(productId: world.Q.Id).Quantity);
    }

    [Fact]
    public void ConcurrentRevisionShowsLockAndCancellingFreesPosition()
    {
        var world = new World();
        var first = world.Inventories.Start(world.A.Id, [world.P.Id]);
        var model = Open(world, _ => { });
        model.Warehouse = world.A;
        model.Planned.Add(world.P);
        model.Start.Execute(null);
        Assert.Contains("блокирует незавершённая ревизия", model.Error);
        Assert.Single(model.Planned);

        model.Selected = model.Entries.Single(x => x.Inventory.Id == first.Id);
        model.Cancel.Execute(null);
        Assert.Equal(InventoryStatus.Cancelled, model.Selected?.Inventory.Status);
        model.New.Execute(null);
        model.Start.Execute(null);
        Assert.True(model.CanEdit);
    }

    private static InventoryViewModel Open(World world, Action<Guid> open) =>
        new(world.Inventories, world.Catalog, world.Documents, world.Reports, true, () => { }, open);
}
