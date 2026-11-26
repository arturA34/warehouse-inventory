using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public enum ReportKind { Balances, Movements, Turnover, Reconciliation }
public sealed record ReportChoice(ReportKind Kind, string Name, IReadOnlyList<string> Columns);
public sealed record ReportWarehouse(Guid? Id, string Name);
public sealed record ReportInventory(Guid Id, string Name);

public sealed class ReportsViewModel : OperationViewModel
{
    private readonly ReportService reports;
    private readonly CatalogService catalog;
    private readonly InventoryService inventories;
    private readonly IReportExporter exporter;
    private ReportChoice selectedReport;
    private ReportWarehouse? selectedWarehouse;
    private ReportInventory? selectedInventory;
    private DateTimeOffset? from;
    private DateTimeOffset? until;
    private bool busy;
    private ReportTable? table;

    internal ReportsViewModel(ReportService reports, CatalogService catalog, InventoryService inventories, IReportExporter exporter)
    {
        this.reports = reports;
        this.catalog = catalog;
        this.inventories = inventories;
        this.exporter = exporter;
        selectedReport = Choices[0];
        var today = DateTimeOffset.Now;
        var quarterMonth = (today.Month - 1) / 3 * 3 + 1;
        from = new DateTimeOffset(today.Year, quarterMonth, 1, 0, 0, 0, today.Offset);
        until = today;
    }

    public IReadOnlyList<ReportChoice> Choices { get; } =
    [
        new(ReportKind.Balances, "Остатки", ["Склад", "Артикул", "Товар", "Количество", "Стоимость", "Средняя цена", "Минимум"]),
        new(ReportKind.Movements, "Движения", ["Дата и время", "Документ", "Склад", "Товар", "Количество", "Стоимость", "Сторно движения"]),
        new(ReportKind.Turnover, "Обороты", ["Склад", "Товар", "Начальный остаток", "Начальная стоимость", "Поступило",
            "Стоимость поступления", "Выбыло", "Стоимость выбытия", "Конечный остаток", "Конечная стоимость"]),
        new(ReportKind.Reconciliation, "Сличительная ведомость", ["Товар", "Учёт", "Учётная стоимость", "Факт", "Разница"])
    ];

    public IReadOnlyList<ReportWarehouse> Warehouses { get; private set; } = [];
    public IReadOnlyList<ReportInventory> Inventories { get; private set; } = [];
    public bool HasPeriod => SelectedReport.Kind is ReportKind.Movements or ReportKind.Turnover;
    public bool HasReconciliation => SelectedReport.Kind == ReportKind.Reconciliation;
    public bool HasTable => Table is not null;
    public string RowCount => Table is null ? "" : $"Строк в отчёте: {Table.Rows.Count}";

    public ReportChoice SelectedReport
    {
        get => selectedReport;
        set
        {
            if (value is null || !Set(ref selectedReport, value)) return;
            Table = null;
            Notify(nameof(HasPeriod));
            Notify(nameof(HasReconciliation));
        }
    }

    public ReportWarehouse? SelectedWarehouse
    {
        get => selectedWarehouse;
        set { if (Set(ref selectedWarehouse, value)) Table = null; }
    }

    public ReportInventory? SelectedInventory
    {
        get => selectedInventory;
        set { if (Set(ref selectedInventory, value)) Table = null; }
    }

    public DateTimeOffset? From { get => from; set { if (Set(ref from, value)) Table = null; } }
    public DateTimeOffset? Until { get => until; set { if (Set(ref until, value)) Table = null; } }
    public bool Busy { get => busy; private set => Set(ref busy, value); }

    public ReportTable? Table
    {
        get => table;
        private set
        {
            if (!Set(ref table, value)) return;
            Notify(nameof(HasTable));
            Notify(nameof(RowCount));
        }
    }

    internal void RefreshSources() => Run(() =>
    {
        var warehouses = catalog.Warehouses().OrderBy(site => site.Name).ToArray();
        var warehouseId = SelectedWarehouse?.Id;
        var inventoryId = SelectedInventory?.Id;
        Warehouses = [new(null, "Все склады"), .. warehouses.Select(site => new ReportWarehouse(site.Id, site.Name))];
        Inventories = inventories.List().OrderByDescending(x => x.StartedAt).Select(x =>
        {
            var name = warehouses.FirstOrDefault(w => w.Id == x.WarehouseId)?.Name ?? x.WarehouseId.ToString();
            return new ReportInventory(x.Id, $"{name} · {x.StartedAt.ToLocalTime():g}");
        }).ToArray();
        Notify(nameof(Warehouses));
        Notify(nameof(Inventories));
        SelectedWarehouse = Warehouses.FirstOrDefault(site => site.Id == warehouseId) ?? Warehouses[0];
        SelectedInventory = Inventories.FirstOrDefault(inv => inv.Id == inventoryId) ?? Inventories.FirstOrDefault();
    });

    public Task GenerateAsync() => PerformAsync(async () =>
    {
        var choice = SelectedReport;
        var warehouseId = SelectedWarehouse?.Id;
        var inventoryId = SelectedInventory?.Id;
        var start = From;
        var finish = Until;
        var result = await Task.Run(() => choice.Kind switch
        {
            ReportKind.Balances => reports.BalanceTable(warehouseId),
            ReportKind.Movements => reports.MovementTable(AsDay(start), AsDay(finish, end: true), warehouseId, useNames: true),
            ReportKind.Turnover => reports.TurnoverTable(AsDay(start), AsDay(finish, end: true), warehouseId, useNames: true),
            ReportKind.Reconciliation => reports.ReconciliationTable(inventoryId ??
                throw new WarehouseException(ErrorCode.Validation, "Выберите ревизию."), useNames: true),
            _ => throw new ArgumentOutOfRangeException(nameof(choice))
        });
        Table = result with { Title = choice.Name, Columns = choice.Columns };
    });

    public Task SaveAsync(ExportFormat format, Func<ReportFile, Task<bool>> write) => PerformAsync(async () =>
    {
        if (Table is not { } preview) return;
        var file = await Task.Run(() => exporter.Export(preview, format));
        if (await write(file)) Status = "Отчёт сохранён.";
    });

    private async Task PerformAsync(Func<Task> operation)
    {
        if (Busy) return;
        Busy = true;
        ClearFeedback();
        try { await operation(); }
        catch (WarehouseException exception) { ShowError(exception); }
        catch (Exception exception) when (IsDataError(exception)) { ShowDataError(); }
        finally { Busy = false; }
    }

    private static DateTimeOffset AsDay(DateTimeOffset? value, bool end = false)
    {
        var day = value?.Date ?? throw new WarehouseException(ErrorCode.Validation, "Укажите обе даты периода.");
        if (end && day == DateTime.MaxValue.Date) return DateTimeOffset.MaxValue;
        var boundary = end ? day.AddDays(1) : day;
        var result = new DateTimeOffset(boundary, TimeZoneInfo.Local.GetUtcOffset(boundary));
        return end ? result.AddTicks(-1) : result;
    }
}
