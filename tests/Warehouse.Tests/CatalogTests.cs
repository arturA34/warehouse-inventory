namespace Warehouse.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void Products_enforce_unique_trimmed_sku_and_barcode_even_when_archived()
    {
        var w = new World();
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.CreateProduct(" p ", "new", "Name", "pcs"));
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.CreateProduct("new", " 00123 ", "Name", "pcs"));
        w.Catalog.SetProductActive(w.P.Id, false);
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.CreateProduct("P", "new", "Name", "pcs"));
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.CreateProduct("new", "00123", "Name", "pcs"));
        var updated = w.Catalog.UpdateProduct(w.P.Id, "p", "00123", "Changed", "kg");
        Assert.False(updated.IsActive);
        Assert.Equal("Changed", w.Catalog.GetProduct(w.P.Id).Name);
        Assert.True(w.Catalog.SetProductActive(w.P.Id, true).IsActive);
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.UpdateProduct(w.P.Id, "Q", "00123", "N", "kg"));
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.UpdateProduct(w.P.Id, "p", "00456", "N", "kg"));
    }

    [Fact]
    public void Search_has_known_results_stable_paging_and_archived_filter()
    {
        var w = new World();
        Assert.Equal(w.P.Id, w.Catalog.FindByBarcode(" 00123 ").Id);
        Check.Error(ErrorCode.NotFound, () => w.Catalog.FindByBarcode("123"));
        Check.Error(ErrorCode.Validation, () => w.Catalog.FindByBarcode(" "));
        Assert.Equal(2, w.Catalog.Search().Total);
        Assert.Equal(w.P.Id, Assert.Single(w.Catalog.Search("p", take: 1).Items).Id);
        Assert.Equal(w.Q.Id, Assert.Single(w.Catalog.Search("", skip: 1, take: 1).Items).Id);
        Assert.Empty(w.Catalog.Search(skip: int.MaxValue).Items);
        Assert.Equal(2, w.Catalog.Search(skip: int.MaxValue).Total);
        Assert.Equal(w.Q.Id, Assert.Single(w.Catalog.Search("00456").Items).Id);
        Assert.Empty(w.Catalog.Search("missing").Items);
        w.Catalog.SetProductActive(w.P.Id, false);
        Assert.Equal(1, w.Catalog.Search().Total);
        Assert.Equal(2, w.Catalog.Search(includeArchived: true).Total);
        Assert.Empty(w.Catalog.Search(" 00123 ").Items);
        Assert.Equal(w.P.Id, Assert.Single(w.Catalog.Search(" 00123 ", true).Items).Id);
        Assert.Single(w.Catalog.Search(take: 500).Items);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 501)]
    public void Search_rejects_invalid_page_bounds(int skip, int take)
    {
        var w = new World();
        Check.Error(ErrorCode.Validation, () => w.Catalog.Search(skip: skip, take: take));
        Check.Error(ErrorCode.Validation, () => w.Catalog.Search(null!));
    }

    [Fact]
    public void Products_with_stock_or_inventory_cannot_be_archived_and_units_preserve_history()
    {
        var w = new World();
        w.Receipt(2, 3);
        Check.Error(ErrorCode.InvalidState, () => w.Catalog.SetProductActive(w.P.Id, false));
        Check.Error(ErrorCode.InvalidState, () => w.Catalog.UpdateProduct(w.P.Id, "P", "00123", "N", "kg"));
        Assert.Equal("pcs", w.Catalog.UpdateProduct(w.P.Id, "P", "00123", "Renamed", "pcs").Unit);
        w.Issue(2);
        Check.Error(ErrorCode.InvalidState, () => w.Catalog.UpdateProduct(w.P.Id, "P", "00123", "N", "kg"));
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        Check.Error(ErrorCode.InventoryLocked, () => w.Catalog.SetProductActive(w.P.Id, false));
        w.Inventories.Cancel(inventory.Id);
        Assert.False(w.Catalog.SetProductActive(w.P.Id, false).IsActive);
    }

    [Fact]
    public void Warehouses_enforce_names_and_archive_guards()
    {
        var w = new World();
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.CreateWarehouse(" a "));
        Check.Error(ErrorCode.Duplicate, () => w.Catalog.UpdateWarehouse(w.A.Id, "b"));
        Assert.Equal("A1", w.Catalog.UpdateWarehouse(w.A.Id, "A1").Name);
        Assert.Equal(2, w.Catalog.Warehouses().Count);
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        Check.Error(ErrorCode.InventoryLocked, () => w.Catalog.UpdateWarehouse(w.A.Id, "A1", false));
        w.Inventories.Cancel(inventory.Id);
        w.Receipt(1, 1);
        Check.Error(ErrorCode.InvalidState, () => w.Catalog.UpdateWarehouse(w.A.Id, "A1", false));
        w.Issue(1);
        Assert.False(w.Catalog.UpdateWarehouse(w.A.Id, "A1", false).IsActive);
        Check.Error(ErrorCode.InvalidState, () => w.Catalog.SetMinimum(w.A.Id, w.P.Id, 1));
        Assert.True(w.Catalog.UpdateWarehouse(w.A.Id, "A1").IsActive);
    }

    [Fact]
    public void Minimum_is_unique_per_product_and_warehouse_and_zero_disables_alert()
    {
        var w = new World();
        var first = w.Catalog.SetMinimum(w.A.Id, w.P.Id, 2);
        Assert.Equal(first.Id, w.Catalog.SetMinimum(w.A.Id, w.P.Id, 0).Id);
        Assert.NotEqual(first.Id, w.Catalog.SetMinimum(w.B.Id, w.P.Id, 3).Id);
        Assert.Equal(2, w.Read(s => s.Minimums.All()).Count);
        Check.Error(ErrorCode.Validation, () => w.Catalog.SetMinimum(w.A.Id, w.P.Id, -1));
        w.Catalog.SetProductActive(w.Q.Id, false);
        Check.Error(ErrorCode.InvalidState, () => w.Catalog.SetMinimum(w.A.Id, w.Q.Id, 1));
    }

    [Fact]
    public void Missing_references_never_create_partial_data()
    {
        var w = new World();
        var missing = Guid.NewGuid();
        Check.Error(ErrorCode.NotFound, () => w.Catalog.GetProduct(missing));
        Check.Error(ErrorCode.NotFound, () => w.Catalog.UpdateProduct(missing, "x", "x", "x", "x"));
        Check.Error(ErrorCode.NotFound, () => w.Catalog.SetProductActive(missing, false));
        Check.Error(ErrorCode.NotFound, () => w.Catalog.UpdateWarehouse(missing, "x"));
        Check.Error(ErrorCode.NotFound, () => w.Catalog.SetMinimum(missing, w.P.Id, 1));
        Check.Error(ErrorCode.NotFound, () => w.Catalog.SetMinimum(w.A.Id, missing, 1));
        Check.Error(ErrorCode.Validation, () => w.Catalog.GetProduct(Guid.Empty));
        Assert.Empty(w.Read(s => s.Minimums.All()));
    }
}
