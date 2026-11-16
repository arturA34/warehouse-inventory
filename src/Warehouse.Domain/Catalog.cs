namespace Warehouse.Domain;

public sealed record Product
{
    public Guid Id { get; }
    public string Sku { get; }
    public string Barcode { get; }
    public string Name { get; }
    public string Unit { get; }
    public bool IsActive { get; }

    public Product(Guid id, string sku, string barcode, string name, string unit, bool isActive = true)
    {
        Id = Rules.Id(id);
        Sku = Rules.Text(sku);
        Barcode = Rules.Text(barcode);
        Name = Rules.Text(name);
        Unit = Rules.Text(unit);
        IsActive = isActive;
    }
}

public sealed record WarehouseSite
{
    public Guid Id { get; }
    public string Name { get; }
    public bool IsActive { get; }

    public WarehouseSite(Guid id, string name, bool isActive = true)
    {
        Id = Rules.Id(id);
        Name = Rules.Text(name);
        IsActive = isActive;
    }
}

public readonly record struct StockKey
{
    public Guid WarehouseId { get; }
    public Guid ProductId { get; }

    public StockKey(Guid warehouseId, Guid productId)
    {
        WarehouseId = Rules.Id(warehouseId);
        ProductId = Rules.Id(productId);
    }

    public void Validate() { Rules.Id(WarehouseId); Rules.Id(ProductId); }
}

public sealed record MinimumStock
{
    public Guid Id { get; }
    public StockKey Key { get; }
    public decimal Quantity { get; }

    public MinimumStock(Guid id, StockKey key, decimal quantity)
    {
        Id = Rules.Id(id);
        key.Validate();
        Key = key;
        Quantity = Rules.Nonnegative(quantity);
    }
}
