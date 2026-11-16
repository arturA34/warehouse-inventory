using Warehouse.Domain;

namespace Warehouse.Application;

public sealed class CatalogService(IUnitOfWork work, ICurrentUser user, IClock clock) : ServiceBase(work, user, clock)
{
    public Product CreateProduct(string sku, string barcode, string name, string unit) => Run(Permission.ManageCatalog, s =>
    {
        var product = new Product(Guid.NewGuid(), sku, barcode, name, unit);
        Unique(s, product);
        s.Products.Add(product);
        Audit(s, "ProductCreated", product.Id);
        return product;
    });

    public Product UpdateProduct(Guid id, string sku, string barcode, string name, string unit) => Run(Permission.ManageCatalog, s =>
    {
        var old = Get(s.Products, id);
        // Changing units of existing stock would change the meaning of historical quantities.
        Rules.Require(old.Unit == Rules.Text(unit) || !s.Movements.All().Any(x => x.Key.ProductId == id),
            ErrorCode.InvalidState, "A product with movements cannot change its unit.");
        var product = new Product(id, sku, barcode, name, unit, old.IsActive);
        Unique(s, product);
        s.Products.Update(product);
        Audit(s, "ProductUpdated", id);
        return product;
    });

    public Product SetProductActive(Guid id, bool active) => Run(Permission.ManageCatalog, s =>
    {
        var old = Get(s.Products, id);
        if (!active)
        {
            Rules.Require(!s.Stock.All().Any(x => x.Key.ProductId == id && x.Quantity != 0), ErrorCode.InvalidState, "A stocked product cannot be archived.");
            Rules.Require(!s.Inventories.All().Any(x => x.Status == InventoryStatus.Active && x.Lines.Any(l => l.ProductId == id)),
                ErrorCode.InventoryLocked, "An inventory product cannot be archived.");
        }
        var product = new Product(id, old.Sku, old.Barcode, old.Name, old.Unit, active);
        s.Products.Update(product);
        Audit(s, active ? "ProductRestored" : "ProductArchived", id);
        return product;
    });

    public Product GetProduct(Guid id) => Run(Permission.Read, s => Get(s.Products, id));
    public Product FindByBarcode(string barcode) => Run(Permission.Read, s =>
        s.Products.ByBarcode(Rules.Text(barcode)) ?? throw new WarehouseException(ErrorCode.NotFound, "Barcode was not found."));

    public ProductPage Search(string query = "", bool includeArchived = false, int skip = 0, int take = 50) => Run(Permission.Read, s =>
    {
        Rules.Require(query != null && skip >= 0 && take is > 0 and <= 500, ErrorCode.Validation, "Invalid search or page bounds.");
        return s.Products.Search(query!.Trim(), includeArchived, skip, take);
    });

    public WarehouseSite CreateWarehouse(string name) => Run(Permission.ManageWarehouses, s =>
    {
        var site = new WarehouseSite(Guid.NewGuid(), name);
        UniqueWarehouse(s, site);
        s.Warehouses.Add(site);
        Audit(s, "WarehouseCreated", site.Id);
        return site;
    });

    public WarehouseSite UpdateWarehouse(Guid id, string name, bool active = true) => Run(Permission.ManageWarehouses, s =>
    {
        Get(s.Warehouses, id);
        var site = new WarehouseSite(id, name, active);
        UniqueWarehouse(s, site);
        if (!active)
        {
            Rules.Require(!s.Stock.All().Any(x => x.Key.WarehouseId == id && x.Quantity != 0), ErrorCode.InvalidState, "A stocked warehouse cannot be archived.");
            Rules.Require(!s.Inventories.All().Any(x => x.WarehouseId == id && x.Status == InventoryStatus.Active),
                ErrorCode.InventoryLocked, "The warehouse has an active inventory.");
        }
        s.Warehouses.Update(site);
        Audit(s, "WarehouseUpdated", id);
        return site;
    });

    public IReadOnlyList<WarehouseSite> Warehouses() => Run(Permission.Read, s => s.Warehouses.All());

    public MinimumStock SetMinimum(Guid warehouseId, Guid productId, decimal quantity) => Run(Permission.ManageCatalog, s =>
    {
        ActiveWarehouse(s, warehouseId);
        ActiveProduct(s, productId);
        var key = new StockKey(warehouseId, productId);
        var previous = s.Minimums.All().SingleOrDefault(x => x.Key == key);
        var threshold = new MinimumStock(previous?.Id ?? Guid.NewGuid(), key, quantity);
        if (previous == null) s.Minimums.Add(threshold); else s.Minimums.Update(threshold);
        Audit(s, "MinimumStockSet", threshold.Id);
        return threshold;
    });

    private static void Unique(IWarehouseSession s, Product product)
    {
        Rules.Require(s.Products.BySku(product.Sku) is not { } sku || sku.Id == product.Id, ErrorCode.Duplicate, "SKU already exists.");
        Rules.Require(s.Products.ByBarcode(product.Barcode) is not { } barcode || barcode.Id == product.Id, ErrorCode.Duplicate, "Barcode already exists.");
    }

    private static void UniqueWarehouse(IWarehouseSession s, WarehouseSite site) =>
        Rules.Require(!s.Warehouses.All().Any(x => x.Id != site.Id && string.Equals(x.Name, site.Name, StringComparison.OrdinalIgnoreCase)),
            ErrorCode.Duplicate, "Warehouse name already exists.");
}
