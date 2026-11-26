using System.Collections.ObjectModel;
using System.Windows.Input;
using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public sealed record DocumentKindChoice(DocumentKind Kind, string Name);
public sealed record DocumentListItem(StockDocument Document, string Title, string Warehouses, string Status);
public sealed record DocumentMovementRow(string Warehouse, string Product, decimal Quantity, decimal Value);
public sealed class DocumentLineDraft(Product product, decimal? quantity = null, decimal? unitCost = null)
{
    public string Item => $"{product.Sku} · {product.Name}";
    public Guid ProductId => product.Id;
    public decimal? Quantity { get; set; } = quantity;
    public decimal? UnitCost { get; set; } = unitCost;

    internal DocumentLine ToLine(bool priced) => new(ProductId,
        Quantity ?? throw new WarehouseException(ErrorCode.Validation, "Введите количество в каждой строке."),
        priced ? UnitCost ?? throw new WarehouseException(ErrorCode.Validation, "Введите цену в каждой строке.") : null);
}

public sealed class DocumentsViewModel : OperationViewModel
{
    private readonly DocumentService documents;
    private readonly CatalogService catalog;
    private readonly ReportService reports;
    private readonly Action balancesChanged;
    private readonly bool canReverse;
    private IReadOnlyList<DocumentListItem> entries = [];
    private IReadOnlyList<DocumentMovementRow> history = [];
    private DocumentListItem? selected;
    private DocumentKindChoice? kind;
    private WarehouseSite? source;
    private WarehouseSite? destination;
    private StockDocument? document;

    internal DocumentsViewModel(DocumentService documents, CatalogService catalog, ReportService reports,
        bool canManage, bool canReverse, Action balancesChanged)
    {
        this.documents = documents;
        this.catalog = catalog;
        this.reports = reports;
        this.balancesChanged = balancesChanged;
        CanManage = canManage;
        this.canReverse = canReverse;
        Picker = new ProductPickerViewModel(catalog);
        Refresh = new Command(() => Run(() => Reload(document)));
        New = new Command(() => Run(() => { if (CanManage) Open(null); }));
        AddLine = new Command(() => Run(AddSelectedProduct));
        RemoveLine = new RemoveDocumentLineCommand(this);
        Save = new Command(() => Run(SaveDraft));
        Post = new Command(() => Run(() =>
        {
            if (!CanPost || document is null) return;
            var posted = documents.Post(document.Id, GetLines());
            RefreshAfterPosting(posted, "Документ проведён.");
        }));
        Reverse = new Command(() => Run(() =>
        {
            if (!CanReverse || document is null) return;
            var reversed = documents.Reverse(document.Id);
            RefreshAfterPosting(reversed, "Документ сторнирован.");
        }));
        Run(() =>
        {
            Reload(null);
            Open(null);
        });
    }

    public IReadOnlyList<DocumentKindChoice> Kinds { get; } =
    [
        new(DocumentKind.Receipt, "Приход"), new(DocumentKind.Issue, "Расход"),
        new(DocumentKind.Transfer, "Перемещение"), new(DocumentKind.InventorySurplus, "Излишки ревизии"),
        new(DocumentKind.InventoryShortage, "Недостача ревизии")
    ];
    public IReadOnlyList<DocumentKindChoice> NewKinds => Kinds.Take(3).ToArray();
    public IReadOnlyList<WarehouseSite> Warehouses { get; private set; } = [];
    public ObservableCollection<DocumentLineDraft> Lines { get; } = [];
    public ProductPickerViewModel Picker { get; }
    public bool CanManage { get; }
    public bool CanEditHeader => CanManage && document is null;
    public bool CanEdit => CanManage && (document is null || document is { Status: DocumentStatus.Draft, InventoryId: null });
    public bool CanPost => CanEdit && document is not null;
    public bool CanReverse => canReverse && document is { Status: DocumentStatus.Posted };
    public bool IsTransfer => Kind?.Kind == DocumentKind.Transfer;
    public bool HasPrice => Kind?.Kind is DocumentKind.Receipt or DocumentKind.InventorySurplus;
    public string Heading => document is null ? "Новый документ" : $"{Kind?.Name} · {StatusName(document.Status)} · № {document.Number?.ToString() ?? "—"}";
    public string WarehouseSummary => document is null ? "" : WarehousePath(document);

    public IReadOnlyList<DocumentListItem> Entries { get => entries; private set => Set(ref entries, value); }
    public IReadOnlyList<DocumentMovementRow> History { get => history; private set => Set(ref history, value); }
    public WarehouseSite? Source { get => source; set => Set(ref source, value); }
    public WarehouseSite? Destination { get => destination; set => Set(ref destination, value); }

    public DocumentKindChoice? Kind
    {
        get => kind;
        set
        {
            if (!Set(ref kind, value)) return;
            Notify(nameof(IsTransfer));
            Notify(nameof(HasPrice));
        }
    }

    public DocumentListItem? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value) || value is null) return;
            Run(() => Open(value.Document));
        }
    }

    public ICommand New { get; }
    public ICommand Refresh { get; }
    public ICommand AddLine { get; }
    public ICommand RemoveLine { get; }
    public ICommand Save { get; }
    public ICommand Post { get; }
    public ICommand Reverse { get; }

    internal void Show(Guid documentId) => Run(() => Reload(documents.GetDocument(documentId)));

    private void Reload(StockDocument? current)
    {
        Warehouses = catalog.Warehouses().OrderBy(site => site.Name).ToArray();
        Notify(nameof(Warehouses));
        Entries = documents.List().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new DocumentListItem(x, $"{Kinds.Single(k => k.Kind == x.Kind).Name} · № {x.Number?.ToString() ?? "—"}",
                WarehousePath(x), StatusName(x.Status))).ToArray();
        Set(ref selected, Entries.FirstOrDefault(x => x.Document.Id == current?.Id), nameof(Selected));
        if (current is not null) Open(current);
    }

    private void Open(StockDocument? value)
    {
        var lineDrafts = value?.Lines.Select(line =>
            new DocumentLineDraft(catalog.GetProduct(line.ProductId), line.Quantity, line.UnitCost)).ToArray() ?? [];
        var movements = value is null || value.Status == DocumentStatus.Draft
            ? []
            : reports.DocumentMovements(value.Id).Select(row => new DocumentMovementRow(
                WarehouseName(row.Key.WarehouseId), lineDrafts.Single(line => line.ProductId == row.Key.ProductId).Item,
                row.Quantity, row.Value)).ToArray();

        document = value;
        if (value is null) Set(ref selected, null, nameof(Selected));
        Kind = value is null ? Kinds[0] : Kinds.Single(x => x.Kind == value.Kind);
        Source = value is null ? Warehouses.FirstOrDefault(x => x.IsActive) : Warehouses.FirstOrDefault(x => x.Id == value.WarehouseId);
        Destination = value is null ? null : Warehouses.FirstOrDefault(x => x.Id == value.DestinationId);
        Lines.Clear();
        foreach (var line in lineDrafts) Lines.Add(line);
        History = movements;
        Notify(nameof(Heading));
        Notify(nameof(WarehouseSummary));
        Notify(nameof(CanEditHeader));
        Notify(nameof(CanEdit));
        Notify(nameof(CanPost));
        Notify(nameof(CanReverse));
    }

    private void AddSelectedProduct()
    {
        if (!CanEdit) return;
        Rules.Require(Picker.Selected is not null, ErrorCode.Validation, "Выберите товар.");
        var product = Picker.Selected!;
        Rules.Require(Lines.All(line => line.ProductId != product.Id), ErrorCode.Duplicate, "Этот товар уже добавлен.");
        Lines.Add(new DocumentLineDraft(product));
    }

    private DocumentLine[] GetLines() => Lines.Select(line => line.ToLine(HasPrice)).ToArray();

    private void SaveDraft()
    {
        if (!CanEdit) return;
        if (document is null)
        {
            Rules.Require(Kind is not null && Source is { IsActive: true }, ErrorCode.Validation, "Выберите тип документа и действующий склад.");
            Rules.Require(!IsTransfer || Destination is { IsActive: true } && Destination.Id != Source!.Id,
                ErrorCode.Validation, "Выберите два разных действующих склада.");
        }

        var lines = GetLines();
        var saved = document is { } prior
            ? documents.Edit(prior.Id, lines)
            : documents.Create(Kind!.Kind, Source!.Id, lines, IsTransfer ? Destination!.Id : null);
        Status = "Черновик сохранён.";
        Reload(saved);
    }

    private void RefreshAfterPosting(StockDocument posted, string message)
    {
        Status = message;
        balancesChanged();
        Reload(posted);
    }

    private string WarehousePath(StockDocument value) => value.DestinationId is { } target
        ? $"{WarehouseName(value.WarehouseId)} → {WarehouseName(target)}"
        : WarehouseName(value.WarehouseId);

    private string WarehouseName(Guid id) => Warehouses.FirstOrDefault(x => x.Id == id)?.Name ?? id.ToString();

    private static string StatusName(DocumentStatus value) => value switch
    {
        DocumentStatus.Draft => "Черновик",
        DocumentStatus.Posted => "Проведён",
        DocumentStatus.Reversed => "Сторнирован",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private sealed class RemoveDocumentLineCommand(DocumentsViewModel model) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => parameter is DocumentLineDraft;
        public void Execute(object? parameter)
        {
            if (model.CanEdit && parameter is DocumentLineDraft row) model.Lines.Remove(row);
        }
    }
}
