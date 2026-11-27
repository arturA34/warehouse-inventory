using System.Globalization;
using Warehouse.Domain;

namespace Warehouse.Application;

public sealed record BalanceRow(StockKey Key, string Sku, string ProductName, string WarehouseName,
    decimal Quantity, decimal Value, decimal AverageCost, decimal Minimum, bool BelowMinimum);
public sealed record TurnoverRow(StockKey Key, decimal OpeningQuantity, decimal OpeningValue,
    decimal ReceivedQuantity, decimal ReceivedValue, decimal IssuedQuantity, decimal IssuedValue,
    decimal ClosingQuantity, decimal ClosingValue);

public sealed class ReportService(IUnitOfWork work, ICurrentUser user, IClock clock, IReportExporter exporter)
    : ServiceBase(work, user, clock)
{
    public IReadOnlyList<BalanceRow> Balances(Guid? warehouseId = null, bool belowMinimumOnly = false) => Run(Permission.Read, s =>
    {
        if (warehouseId is { } id) Get(s.Warehouses, id);
        var stock = s.Stock.All().ToDictionary(x => x.Key);
        var thresholds = s.Minimums.All().ToDictionary(x => x.Key, x => x.Quantity);
        var products = s.Products.All().ToDictionary(x => x.Id);
        var warehouses = s.Warehouses.All().ToDictionary(x => x.Id);
        // Если для товара задан минимум, нулевой остаток тоже должен попасть в отчёт.
        return stock.Keys.Union(thresholds.Keys).Where(x => warehouseId == null || x.WarehouseId == warehouseId)
            .Select(key =>
            {
                var balance = stock.GetValueOrDefault(key) ?? new StockBalance(key);
                var minimum = thresholds.GetValueOrDefault(key);
                return new BalanceRow(key, products[key.ProductId].Sku, products[key.ProductId].Name,
                    warehouses[key.WarehouseId].Name, balance.Quantity, balance.Value, balance.AverageCost, minimum, balance.Quantity < minimum);
            }).Where(x => !belowMinimumOnly || x.BelowMinimum).OrderBy(x => x.WarehouseName, StringComparer.Ordinal)
            .ThenBy(x => x.Sku, StringComparer.Ordinal).ToArray();
    });

    public IReadOnlyList<StockMovement> Movements(DateTimeOffset from, DateTimeOffset to, Guid? warehouseId = null, Guid? productId = null) =>
        Run(Permission.Read, s =>
        {
            ValidateRange(s, from, to, warehouseId, productId);
            return Filter(s, warehouseId, productId).Where(x => x.OccurredAt >= from && x.OccurredAt <= to)
                .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToArray();
        });

    public IReadOnlyList<StockMovement> DocumentMovements(Guid documentId) => Run(Permission.Read, s =>
    {
        Get(s.Documents, documentId);
        return s.Movements.All().Where(x => x.DocumentId == documentId)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.ReversesMovementId != null).ThenBy(x => x.Id).ToArray();
    });

    /// <summary>Начальный остаток считаем до начала периода. В обороты входят обе границы периода.</summary>
    public IReadOnlyList<TurnoverRow> Turnover(DateTimeOffset from, DateTimeOffset to, Guid? warehouseId = null, Guid? productId = null) =>
        Run(Permission.Read, s =>
        {
            ValidateRange(s, from, to, warehouseId, productId);
            return Filter(s, warehouseId, productId).Where(x => x.OccurredAt <= to).GroupBy(x => x.Key).Select(group =>
            {
                var before = group.Where(x => x.OccurredAt < from);
                var received = group.Where(x => x.OccurredAt >= from && x.Quantity > 0);
                var issued = group.Where(x => x.OccurredAt >= from && x.Quantity < 0);
                decimal Sum(IEnumerable<StockMovement> items, Func<StockMovement, decimal> selector) => Rules.Sum(items.Select(selector));
                return new TurnoverRow(group.Key, Sum(before, x => x.Quantity), Sum(before, x => x.Value),
                    Sum(received, x => x.Quantity), Sum(received, x => x.Value),
                    -Sum(issued, x => x.Quantity), -Sum(issued, x => x.Value),
                    Sum(group, x => x.Quantity), Sum(group, x => x.Value));
            }).OrderBy(x => x.Key.WarehouseId).ThenBy(x => x.Key.ProductId).ToArray();
        });

    public InventorySession Reconciliation(Guid inventoryId) => Run(Permission.Read, s => Get(s.Inventories, inventoryId));

    // Данные отчёта получаем в транзакции, а файл формируем уже после её закрытия.
    public ReportFile ExportBalances(ExportFormat format, Guid? warehouseId = null) =>
        Export(format, () => BalanceTable(warehouseId));

    public ReportFile ExportMovements(ExportFormat format, DateTimeOffset from, DateTimeOffset to, Guid? warehouseId = null) =>
        Export(format, () => MovementTable(from, to, warehouseId));

    public ReportFile ExportTurnover(ExportFormat format, DateTimeOffset from, DateTimeOffset to, Guid? warehouseId = null) =>
        Export(format, () => TurnoverTable(from, to, warehouseId));

    public ReportFile ExportReconciliation(ExportFormat format, Guid inventoryId) =>
        Export(format, () => ReconciliationTable(inventoryId));

    public ReportTable BalanceTable(Guid? warehouseId = null)
    {
        var rows = Balances(warehouseId).Select(x => Row(x.WarehouseName, x.Sku, x.ProductName,
            N(x.Quantity), N(x.Value), N(x.AverageCost), N(x.Minimum))).ToArray();
        return new("Stock balances", ["Warehouse", "SKU", "Product", "Quantity", "Value", "Average cost", "Minimum"], rows)
            { NumericColumns = [3, 4, 5, 6] };
    }

    public ReportTable MovementTable(DateTimeOffset from, DateTimeOffset to, Guid? warehouseId = null, bool useNames = false)
    {
        var movements = Movements(from, to, warehouseId);
        var names = useNames ? ReadNames() : null;
        var rows = movements.Select(x => Row(x.OccurredAt.ToString("O"), x.DocumentId.ToString(),
            names?.Warehouse(x.Key.WarehouseId) ?? x.Key.WarehouseId.ToString(),
            names?.Product(x.Key.ProductId) ?? x.Key.ProductId.ToString(),
            N(x.Quantity), N(x.Value), x.ReversesMovementId?.ToString() ?? "")).ToArray();
        return new("Stock movements", ["Time", "Document", "Warehouse", "Product", "Quantity", "Value", "Reverses"], rows)
            { NumericColumns = [4, 5] };
    }

    public ReportTable TurnoverTable(DateTimeOffset from, DateTimeOffset to, Guid? warehouseId = null, bool useNames = false)
    {
        var turnover = Turnover(from, to, warehouseId);
        var names = useNames ? ReadNames() : null;
        var rows = turnover.Select(x => Row(names?.Warehouse(x.Key.WarehouseId) ?? x.Key.WarehouseId.ToString(),
            names?.Product(x.Key.ProductId) ?? x.Key.ProductId.ToString(),
            N(x.OpeningQuantity), N(x.OpeningValue), N(x.ReceivedQuantity), N(x.ReceivedValue), N(x.IssuedQuantity), N(x.IssuedValue),
            N(x.ClosingQuantity), N(x.ClosingValue))).ToArray();
        return new("Stock turnover", ["Warehouse", "Product", "Opening quantity", "Opening value", "Received quantity",
            "Received value", "Issued quantity", "Issued value", "Closing quantity", "Closing value"], rows)
            { NumericColumns = [2, 3, 4, 5, 6, 7, 8, 9] };
    }

    public ReportTable ReconciliationTable(Guid inventoryId, bool useNames = false)
    {
        var inventory = Reconciliation(inventoryId);
        var names = useNames ? ReadNames() : null;
        var rows = inventory.Lines.Select(x => Row(names?.Product(x.ProductId) ?? x.ProductId.ToString(),
            N(x.BookQuantity), N(x.BookValue), x.ActualQuantity is { } actual ? N(actual) : "",
            x.Difference is { } difference ? N(difference) : "")).ToArray();
        return new("Inventory reconciliation", ["Product", "Book quantity", "Book value", "Actual quantity", "Difference"], rows)
            { NumericColumns = [1, 2, 3, 4] };
    }

    private ReportFile Export(ExportFormat format, Func<ReportTable> prepare)
    {
        Rules.Defined(format);
        // Файл формируем после закрытия транзакции чтения.
        return exporter.Export(prepare(), format);
    }

    private ReportNames ReadNames() => Run(Permission.Read, s => new ReportNames(
        s.Warehouses.All().ToDictionary(site => site.Id, site => site.Name),
        s.Products.All().ToDictionary(product => product.Id, product => $"{product.Sku} · {product.Name}")));

    private sealed record ReportNames(Dictionary<Guid, string> Warehouses, Dictionary<Guid, string> Products)
    {
        public string Warehouse(Guid id) => Warehouses.GetValueOrDefault(id) ?? id.ToString();
        public string Product(Guid id) => Products.GetValueOrDefault(id) ?? id.ToString();
    }

    private static string N(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static IReadOnlyList<string> Row(params string[] cells) => Array.AsReadOnly(cells);
    private static IEnumerable<StockMovement> Filter(IWarehouseSession s, Guid? warehouseId, Guid? productId) =>
        s.Movements.All().Where(x => (warehouseId == null || x.Key.WarehouseId == warehouseId) && (productId == null || x.Key.ProductId == productId));

    private static void ValidateRange(IWarehouseSession s, DateTimeOffset from, DateTimeOffset to, Guid? warehouseId, Guid? productId)
    {
        Rules.Require(from <= to, ErrorCode.Validation, "Invalid date range.");
        if (warehouseId is { } warehouse) Get(s.Warehouses, warehouse);
        if (productId is { } product) Get(s.Products, product);
    }
}
