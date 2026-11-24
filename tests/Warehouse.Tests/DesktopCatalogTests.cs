using Warehouse.Desktop.Storage;
using Warehouse.Desktop.ViewModels;

namespace Warehouse.Tests;

public sealed class DesktopCatalogTests
{
    [Fact]
    public async Task FindsProductsBySkuNameAndBarcodeAndCanShowArchive()
    {
        var catalog = await OpenCatalog();
        foreach (var query in new[] { "up-001", "упаковочная", "00123" })
        {
            catalog.Query = query;
            catalog.Search.Execute(null);
            Assert.Equal("00123", Assert.Single(catalog.Products).Barcode);
        }

        catalog.Query = "00789";
        catalog.Search.Execute(null);
        Assert.Empty(catalog.Products);

        catalog.IncludeArchived = true;
        catalog.Search.Execute(null);
        Assert.False(Assert.Single(catalog.Products).IsActive);
    }

    [Fact]
    public void PagingUsesAppliedSearchUntilTheNextSearch()
    {
        var world = new World();
        for (var i = 0; i < 26; i++)
            world.Catalog.CreateProduct($"BOX-{i:00}", $"000{i:00}", $"Коробка {i}", "шт.");
        var catalog = new CatalogViewModel(world.Catalog, world.Reports, true) { Query = "BOX" };
        catalog.Search.Execute(null);
        Assert.Equal(25, catalog.Products.Count);
        Assert.True(catalog.HasNextPage);

        catalog.Query = "unsaved search";
        catalog.NextPage.Execute(null);
        Assert.Equal("BOX-25", Assert.Single(catalog.Products).Sku);
        Assert.True(catalog.HasPreviousPage);
        Assert.False(catalog.HasNextPage);

        catalog.PreviousPage.Execute(null);
        Assert.Equal("BOX-00", catalog.Products[0].Sku);
    }

    [Fact]
    public async Task EditsProductAndShowsWarningOnlyWhenStockIsBelowMinimum()
    {
        var catalog = await OpenCatalog();
        catalog.NewProduct.Execute(null);
        catalog.Draft.Sku = "NEW-1";
        catalog.Draft.Barcode = "00077";
        catalog.Draft.Name = "Новая позиция";
        catalog.Draft.Unit = "шт.";
        catalog.SaveProduct.Execute(null);
        var product = Assert.IsType<Warehouse.Domain.Product>(catalog.SelectedProduct);
        Assert.Equal("00077", product.Barcode);

        catalog.Draft.Name = "Обновлённое название";
        catalog.SaveProduct.Execute(null);
        Assert.Equal(product.Id, catalog.SelectedProduct?.Id);
        Assert.Equal("Обновлённое название", catalog.SelectedProduct?.Name);

        var equal = Assert.Single(catalog.VisibleBalances, row => row.Sku == "UP-001");
        Assert.Equal(equal.Minimum, equal.Quantity);
        Assert.False(equal.BelowMinimum);
        catalog.ShowWarningsOnly = true;
        Assert.Equal(0, Assert.Single(catalog.VisibleBalances).Quantity);

        catalog.SelectedWarehouse = Assert.Single(catalog.Warehouses, site => site.Name == "Основной склад");
        catalog.Minimum = 0.5m;
        catalog.SaveMinimum.Execute(null);
        Assert.Contains(catalog.VisibleBalances, row => row.Key.ProductId == product.Id && row.Quantity == 0 && row.Minimum == 0.5m);

        catalog.Minimum = 0;
        catalog.SaveMinimum.Execute(null);
        Assert.DoesNotContain(catalog.VisibleBalances, row => row.Key.ProductId == product.Id);
    }

    [Fact]
    public async Task AuditorCanReadButCannotSave()
    {
        var catalog = await OpenCatalog("auditor");
        catalog.SelectedProduct = catalog.Products[0];
        var name = catalog.SelectedProduct.Name;
        catalog.Draft.Name = "Изменённое название";
        catalog.SaveProduct.Execute(null);
        catalog.Search.Execute(null);

        Assert.Contains(catalog.Products, product => product.Name == name);
        Assert.False(catalog.CanManageCatalog);
        Assert.False(catalog.CanSetMinimum);
    }

    private static async Task<CatalogViewModel> OpenCatalog(string login = "keeper")
    {
        var main = new MainViewModel(new DesktopServices(new DemoStorage().Connect));
        main.SignIn.Login = login;
        await main.SignIn.SignInAsync("demo");
        return Assert.IsType<WorkspaceViewModel>(main.Screen).Catalog;
    }
}
