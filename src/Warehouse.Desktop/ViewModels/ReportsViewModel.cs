using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public enum ReportKind { Balances, Movements, Turnover, Reconciliation, DeliveryForm, InventoryForm }
public sealed record ReportChoice(ReportKind Kind, string Name, IReadOnlyList<string> Columns);
public sealed record ReportWarehouse(Guid? Id, string Name);
public sealed record ReportInventory(Guid Id, string Name, bool Completed);
public sealed record ReportDocument(Guid Id, string Name);

public sealed class ReportsViewModel : OperationViewModel
{
    private readonly ReportService reports;
    private readonly CatalogService catalog;
    private readonly InventoryService inventories;
    private readonly IReportExporter exporter;
    private readonly PrintFormService forms;
    private readonly DocumentService documents;
    private ReportChoice selectedReport;
    private ReportWarehouse? selectedWarehouse;
    private ReportInventory? selectedInventory;
    private ReportDocument? selectedDocument;
    private DateTimeOffset? from;
    private DateTimeOffset? until;
    private bool busy;
    private ReportTable? table;

    internal ReportsViewModel(ReportService reports, CatalogService catalog, InventoryService inventories,
        IReportExporter exporter, PrintFormService forms, DocumentService documents)
    {
        this.reports = reports;
        this.catalog = catalog;
        this.inventories = inventories;
        this.exporter = exporter;
        this.forms = forms;
        this.documents = documents;
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
        new(ReportKind.Reconciliation, "Сличительная ведомость", ["Товар", "Учёт", "Учётная стоимость", "Факт", "Разница"]),
        new(ReportKind.DeliveryForm, "Проект ТОРГ-12", []),
        new(ReportKind.InventoryForm, "Проект ИНВ-19", [])
    ];

    public IReadOnlyList<ReportWarehouse> Warehouses { get; private set; } = [];
    public IReadOnlyList<ReportInventory> Inventories { get; private set; } = [];
    public IReadOnlyList<ReportDocument> Documents { get; private set; } = [];
    public IReadOnlyList<ReportInventory> AvailableInventories => SelectedReport.Kind == ReportKind.InventoryForm
        ? Inventories.Where(inv => inv.Completed).ToArray() : Inventories;
    public bool HasPeriod => SelectedReport.Kind is ReportKind.Movements or ReportKind.Turnover;
    public bool HasReconciliation => SelectedReport.Kind is ReportKind.Reconciliation or ReportKind.InventoryForm;
    public bool HasDocument => SelectedReport.Kind == ReportKind.DeliveryForm;
    public bool HasWarehouse => !HasReconciliation && !HasDocument;
    public bool HasTable => Table is not null;
    public bool HasExcel => Table is { DraftPrintForm: false };
    public IReadOnlyList<string> Details => Table?.Details ?? [];
    public string Totals => Table?.Totals is { } total
        ? string.Join(" · ", Table.NumericColumns.Where(index => index < total.Count && total[index].Length != 0)
            .Select(index => $"{Table.Columns[index]} {total[index]}"))
        : "";
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
            Notify(nameof(HasDocument));
            Notify(nameof(HasWarehouse));
            Notify(nameof(AvailableInventories));
            SelectInventory(SelectedInventory?.Id);
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

    public ReportDocument? SelectedDocument
    {
        get => selectedDocument;
        set { if (Set(ref selectedDocument, value)) Table = null; }
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
            Notify(nameof(HasExcel));
            Notify(nameof(Details));
            Notify(nameof(Totals));
        }
    }

    internal void RefreshSources() => Run(() =>
    {
        var warehouses = catalog.Warehouses().OrderBy(site => site.Name).ToArray();
        var warehouseId = SelectedWarehouse?.Id;
        var inventoryId = SelectedInventory?.Id;
        var documentId = SelectedDocument?.Id;
        Warehouses = [new(null, "Все склады"), .. warehouses.Select(site => new ReportWarehouse(site.Id, site.Name))];
        Inventories = inventories.List().OrderByDescending(x => x.StartedAt).Select(x =>
        {
            var name = warehouses.FirstOrDefault(w => w.Id == x.WarehouseId)?.Name ?? x.WarehouseId.ToString();
            return new ReportInventory(x.Id, $"{name} · {x.StartedAt.ToLocalTime():g}", x.Status == InventoryStatus.Completed);
        }).ToArray();
        Documents = documents.List().Where(PrintFormService.CanPrintDelivery).OrderByDescending(x => x.PostedAt)
            .Select(x => new ReportDocument(x.Id, $"№ {x.Number} · {x.PostedAt!.Value.ToLocalTime():g}")).ToArray();
        Notify(nameof(Warehouses));
        Notify(nameof(AvailableInventories));
        Notify(nameof(Documents));
        SelectedWarehouse = Warehouses.FirstOrDefault(site => site.Id == warehouseId) ?? Warehouses[0];
        SelectInventory(inventoryId);
        SelectedDocument = Documents.FirstOrDefault(doc => doc.Id == documentId) ?? Documents.FirstOrDefault();
    });

    private void SelectInventory(Guid? id)
    {
        var available = AvailableInventories;
        SelectedInventory = available.FirstOrDefault(inv => inv.Id == id) ?? available.FirstOrDefault();
    }

    public Task GenerateAsync() => PerformAsync(async () =>
    {
        var choice = SelectedReport;
        var warehouseId = SelectedWarehouse?.Id;
        var inventoryId = SelectedInventory?.Id;
        var documentId = SelectedDocument?.Id;
        var start = From;
        var finish = Until;
        var result = await Task.Run(() => choice.Kind switch
        {
            ReportKind.Balances => reports.BalanceTable(warehouseId),
            ReportKind.Movements => reports.MovementTable(AsDay(start), AsDay(finish, end: true), warehouseId, useNames: true),
            ReportKind.Turnover => reports.TurnoverTable(AsDay(start), AsDay(finish, end: true), warehouseId, useNames: true),
            ReportKind.Reconciliation => reports.ReconciliationTable(inventoryId ??
                throw new WarehouseException(ErrorCode.Validation, "Выберите ревизию."), useNames: true),
            ReportKind.DeliveryForm => forms.Delivery(documentId ??
                throw new WarehouseException(ErrorCode.Validation, "Выберите складской документ.")),
            ReportKind.InventoryForm => forms.Reconciliation(inventoryId ??
                throw new WarehouseException(ErrorCode.Validation, "Выберите завершённую ревизию.")),
            _ => throw new ArgumentOutOfRangeException(nameof(choice))
        });
        Table = result.DraftPrintForm ? result : result with { Title = choice.Name, Columns = choice.Columns };
    });

    public Task SaveAsync(ExportFormat format, Func<ReportFile, Task<bool>> write) => PerformAsync(async () =>
    {
        if (Table is not { } preview) return;
        if (format == ExportFormat.Excel && preview.DraftPrintForm) return;
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
