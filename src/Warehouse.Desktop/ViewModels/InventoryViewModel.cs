using System.Collections.ObjectModel;
using System.Windows.Input;
using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public sealed record InventoryListItem(InventorySession Inventory, string Name, string Status, string Date);
public sealed record InventoryCountRow(Product Product, InventoryLine Line)
{
    public string Name => $"{Product.Sku} · {Product.Name}";
    public string Actual => Line.ActualQuantity?.ToString() ?? "Не пересчитано";
    public string Difference => Line.Difference?.ToString() ?? "—";
}
public sealed record InventoryAct(Guid Id, string Name);

public sealed class InventoryViewModel : OperationViewModel
{
    private readonly InventoryService inventories;
    private readonly CatalogService catalog;
    private readonly DocumentService documents;
    private readonly ReportService reports;
    private readonly Action balancesChanged;
    private readonly Dictionary<Guid, Product> productNames = [];
    private IReadOnlyList<InventoryListItem> entries = [];
    private IReadOnlyList<InventoryCountRow> rows = [];
    private IReadOnlyList<InventoryAct> acts = [];
    private InventoryListItem? selected;
    private InventorySession? inventory;
    private InventoryCountRow? currentRow;
    private decimal? actual;
    private decimal? cost;
    private string? barcode;
    private InventoryAct? selectedAct;

    internal InventoryViewModel(InventoryService inventories, CatalogService catalog, DocumentService documents,
        ReportService reports, bool canCount, Action balancesChanged, Action<Guid> openDocument)
    {
        this.inventories = inventories;
        this.catalog = catalog;
        this.documents = documents;
        this.reports = reports;
        this.balancesChanged = balancesChanged;
        CanCount = canCount;
        Picker = new ProductPickerViewModel(catalog);
        Refresh = new Command(() => Run(() =>
        {
            productNames.Clear();
            Reload(inventory is null ? null : inventories.GetInventory(inventory.Id), CurrentRow?.Product.Id);
        }));
        New = new Command(() => Run(() => { if (CanCount) Open(null); }));
        AddProduct = new Command(() => Run(AddSelected));
        RemoveProduct = new Command(() => { if (CanStart && PlannedProduct is { } product) Planned.Remove(product); });
        Start = new Command(() => Run(StartInventory));
        SaveCount = new Command(() => Run(CountSelected));
        Scan = new Command(() => Run(ScanBarcode));
        Complete = new Command(() => Run(CompleteInventory));
        Cancel = new Command(() => Run(() =>
        {
            if (CanEdit) Finish(inventories.Cancel(inventory!.Id), "Ревизия отменена.");
        }));
        OpenAct = new Command(() => { if (SelectedAct is { } act) openDocument(act.Id); });
        Run(() => Reload(null));
    }

    public bool CanCount { get; }
    public bool CanStart => CanCount && inventory is null;
    public bool CanEdit => CanCount && inventory?.Status == InventoryStatus.Active;
    public bool HasInventory => inventory is not null;
    public bool HasActs => Acts.Count > 0;
    public bool CanEnterCount => CanEdit && CurrentRow is not null;
    public bool NeedsCost => CurrentRow?.Line.BookQuantity == 0;
    public string Heading => inventory is null ? "Новая ревизия" : $"Сличительная ведомость · {StatusName(inventory.Status)}";
    public IReadOnlyList<WarehouseSite> Warehouses { get; private set; } = [];
    public ObservableCollection<Product> Planned { get; } = [];
    public ProductPickerViewModel Picker { get; }

    public WarehouseSite? Warehouse { get; set; }
    public Product? PlannedProduct { get; set; }
    public IReadOnlyList<InventoryListItem> Entries { get => entries; private set => Set(ref entries, value); }
    public IReadOnlyList<InventoryCountRow> Rows { get => rows; private set => Set(ref rows, value); }
    public IReadOnlyList<InventoryAct> Acts { get => acts; private set => Set(ref acts, value); }
    public InventoryAct? SelectedAct { get => selectedAct; set => Set(ref selectedAct, value); }
    public string? Barcode { get => barcode; set => Set(ref barcode, value); }
    public decimal? Actual { get => actual; set => Set(ref actual, value); }
    public decimal? Cost { get => cost; set => Set(ref cost, value); }

    public InventoryListItem? Selected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value) && value is not null) Run(() => Open(value.Inventory));
        }
    }

    public InventoryCountRow? CurrentRow
    {
        get => currentRow;
        set
        {
            if (!Set(ref currentRow, value)) return;
            Actual = value?.Line.ActualQuantity;
            Cost = value?.Line.SurplusUnitCost;
            Notify(nameof(NeedsCost));
            Notify(nameof(CanEnterCount));
        }
    }

    public ICommand Refresh { get; }
    public ICommand New { get; }
    public ICommand AddProduct { get; }
    public ICommand RemoveProduct { get; }
    public ICommand Start { get; }
    public ICommand SaveCount { get; }
    public ICommand Scan { get; }
    public ICommand Complete { get; }
    public ICommand Cancel { get; }
    public ICommand OpenAct { get; }

    private void Reload(InventorySession? value, Guid? productId = null)
    {
        Warehouses = catalog.Warehouses().OrderBy(site => site.Name).ToArray();
        Notify(nameof(Warehouses));
        Entries = inventories.List().OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id)
            .Select(x => new InventoryListItem(x, Warehouses.FirstOrDefault(w => w.Id == x.WarehouseId)?.Name ?? x.WarehouseId.ToString(),
                StatusName(x.Status), x.StartedAt.ToLocalTime().ToString("g"))).ToArray();
        Set(ref selected, Entries.FirstOrDefault(x => x.Inventory.Id == value?.Id), nameof(Selected));
        Open(value, productId);
    }

    private void Open(InventorySession? value, Guid? productId = null)
    {
        var data = value?.Status == InventoryStatus.Completed ? reports.Reconciliation(value.Id) : value;
        var newRows = data?.Lines.Select(line => new InventoryCountRow(GetProduct(line.ProductId), line)).ToArray() ?? [];
        var newActs = data?.AdjustmentDocumentIds.Select(id =>
        {
            var document = documents.GetDocument(id);
            var name = document.Kind == DocumentKind.InventorySurplus ? "Акт излишков" : "Акт недостачи";
            return new InventoryAct(id, $"{name} № {document.Number}");
        }).ToArray() ?? [];

        inventory = data;
        if (data is null) Set(ref selected, null, nameof(Selected));
        if (data?.Status != InventoryStatus.Active) Barcode = "";
        Rows = newRows;
        Acts = newActs;
        SelectedAct = Acts.FirstOrDefault();
        CurrentRow = Rows.FirstOrDefault(row => row.Product.Id == productId);
        Notify(nameof(Heading));
        Notify(nameof(CanStart));
        Notify(nameof(CanEdit));
        Notify(nameof(HasInventory));
        Notify(nameof(HasActs));
        Notify(nameof(CanEnterCount));
    }

    private Product GetProduct(Guid id)
    {
        if (!productNames.TryGetValue(id, out var product))
        {
            product = catalog.GetProduct(id);
            productNames.Add(id, product);
        }
        return product;
    }

    private void AddSelected()
    {
        if (!CanStart) return;
        Rules.Require(Picker.Selected is not null, ErrorCode.Validation, "Выберите товар.");
        var product = Picker.Selected!;
        Rules.Require(Planned.All(x => x.Id != product.Id), ErrorCode.Duplicate, "Этот товар уже выбран.");
        Planned.Add(product);
    }

    private void StartInventory()
    {
        if (!CanStart) return;
        Rules.Require(Warehouse is { IsActive: true } && Planned.Count > 0, ErrorCode.Validation,
            "Выберите действующий склад и хотя бы один товар.");
        var result = inventories.Start(Warehouse!.Id, Planned.Select(x => x.Id));
        Planned.Clear();
        Status = "Ревизия открыта.";
        Reload(result);
    }

    private void CountSelected()
    {
        if (!CanEnterCount || CurrentRow is not { } row) return;
        Rules.Require(Actual is not null, ErrorCode.Validation, "Введите фактическое количество. Если товара нет, введите 0.");
        var result = inventories.Count(inventory!.Id, row.Product.Id, Actual!.Value, NeedsCost ? Cost : null);
        Reload(result, row.Product.Id);
        Status = "Факт сохранён.";
    }

    private void ScanBarcode()
    {
        if (!CanEdit) return;
        Rules.Require(!string.IsNullOrWhiteSpace(Barcode), ErrorCode.Validation, "Введите или отсканируйте штрихкод.");
        var product = catalog.FindByBarcode(Barcode!);
        Rules.Require(Rows.Any(x => x.Product.Id == product.Id), ErrorCode.NotFound, "Товар не включён в эту ревизию.");
        var result = inventories.Scan(inventory!.Id, Barcode!);
        Barcode = "";
        Reload(result, product.Id);
        Status = $"Добавлена 1 единица · {product.Sku}";
    }

    private void CompleteInventory()
    {
        if (!CanEdit) return;
        Rules.Require(inventory!.Lines.All(x => x.ActualQuantity.HasValue), ErrorCode.Validation,
            "Пересчитайте все позиции, включая позиции с нулевым фактом.");
        Rules.Require(inventory.Lines.All(x => x.BookQuantity != 0 || x.Difference <= 0 || x.SurplusUnitCost.HasValue),
            ErrorCode.Validation, "Введите цену для излишков без учётного остатка.");
        Finish(inventories.Complete(inventory.Id), "Ревизия завершена. Сличительная ведомость доступна.");
    }

    private void Finish(InventorySession result, string message)
    {
        Status = message;
        balancesChanged();
        Reload(result);
    }

    private static string StatusName(InventoryStatus status) => status switch
    {
        InventoryStatus.Active => "Открыта",
        InventoryStatus.Completed => "Завершена",
        InventoryStatus.Cancelled => "Отменена",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
