using System.Data.Common;
using System.Windows.Input;
using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public sealed class CatalogViewModel : ViewModel
{
    private const int PageSize = 25;
    private readonly CatalogService catalog;
    private readonly ReportService reports;
    private IReadOnlyList<BalanceRow> balances = [];
    private string appliedQuery = "";
    private bool appliedArchive;
    private int offset;
    private int total;
    private bool showWarningsOnly;
    private Product? selectedProduct;
    private WarehouseSite? selectedWarehouse;
    private ProductDraft draft = new();
    private decimal? minimum;
    private string error = "";
    private string status = "";

    internal CatalogViewModel(CatalogService catalog, ReportService reports, bool canManageCatalog)
    {
        this.catalog = catalog;
        this.reports = reports;
        CanManageCatalog = canManageCatalog;
        Search = new Command(() => Run(ApplySearch));
        PreviousPage = new Command(() => Run(() =>
        {
            if (HasPreviousPage) LoadPage(offset - PageSize, appliedQuery, appliedArchive);
        }));
        NextPage = new Command(() => Run(() =>
        {
            if (HasNextPage) LoadPage(offset + PageSize, appliedQuery, appliedArchive);
        }));
        NewProduct = new Command(ResetEditor);
        SaveProduct = new Command(() => Run(SaveDraft));
        SaveMinimum = new Command(() => Run(SetMinimum));
        RefreshBalances = new Command(() => Run(LoadBalances));
        Run(() =>
        {
            Warehouses = catalog.Warehouses().Where(site => site.IsActive)
                .OrderBy(site => site.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            ApplySearch();
            LoadBalances();
        });
    }

    public bool CanManageCatalog { get; }
    public string? Query { get; set; }
    public bool IncludeArchived { get; set; }
    public IReadOnlyList<Product> Products { get; private set; } = [];
    public IReadOnlyList<WarehouseSite> Warehouses { get; private set; } = [];
    public IReadOnlyList<BalanceRow> VisibleBalances => ShowWarningsOnly
        ? balances.Where(row => row.BelowMinimum).ToArray()
        : balances;

    public bool ShowWarningsOnly
    {
        get => showWarningsOnly;
        set
        {
            if (Set(ref showWarningsOnly, value)) Notify(nameof(VisibleBalances));
        }
    }

    public bool HasPreviousPage => offset > 0;
    public bool HasNextPage => total - offset > PageSize;
    public string PageLabel => total == 0 ? "Товары не найдены" :
        $"{offset + 1}–{offset + Products.Count} из {total}";

    public Product? SelectedProduct
    {
        get => selectedProduct;
        set
        {
            if (Set(ref selectedProduct, value)) LoadEditor();
        }
    }

    public WarehouseSite? SelectedWarehouse
    {
        get => selectedWarehouse;
        set
        {
            if (Set(ref selectedWarehouse, value)) LoadMinimum();
        }
    }

    public ProductDraft Draft { get => draft; private set => Set(ref draft, value); }
    public bool HasEditor => CanManageCatalog || SelectedProduct is not null;
    public string EditorTitle => SelectedProduct is null ? "Новый товар" : "Карточка товара";
    public bool CanSetMinimum => CanManageCatalog && SelectedProduct is { IsActive: true };
    public decimal? Minimum { get => minimum; set => Set(ref minimum, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string Status { get => status; private set => Set(ref status, value); }

    public ICommand Search { get; }
    public ICommand PreviousPage { get; }
    public ICommand NextPage { get; }
    public ICommand NewProduct { get; }
    public ICommand SaveProduct { get; }
    public ICommand SaveMinimum { get; }
    public ICommand RefreshBalances { get; }

    private void ApplySearch() => LoadPage(0, Query ?? "", IncludeArchived);

    private void LoadPage(int newOffset, string query, bool includeArchived, Guid? keepSelection = null)
    {
        var page = catalog.Search(query, includeArchived, newOffset, PageSize);
        if (newOffset > 0 && newOffset >= page.Total)
        {
            newOffset = page.Total == 0 ? 0 : (page.Total - 1) / PageSize * PageSize;
            page = catalog.Search(query, includeArchived, newOffset, PageSize);
        }

        offset = newOffset;
        total = page.Total;
        appliedQuery = query;
        appliedArchive = includeArchived;
        Products = page.Items;
        Notify(nameof(Products));
        Notify(nameof(PageLabel));
        Notify(nameof(HasPreviousPage));
        Notify(nameof(HasNextPage));
        SelectedProduct = Products.FirstOrDefault(product => product.Id == keepSelection);
        if (keepSelection.HasValue) LoadEditor();
    }

    private void ResetEditor()
    {
        Error = Status = "";
        SelectedProduct = null;
        LoadEditor();
    }

    private void LoadEditor()
    {
        Draft = SelectedProduct is { } product
            ? new ProductDraft { Sku = product.Sku, Barcode = product.Barcode, Name = product.Name, Unit = product.Unit }
            : new ProductDraft();
        Notify(nameof(HasEditor));
        Notify(nameof(EditorTitle));
        Notify(nameof(CanSetMinimum));
        LoadMinimum();
    }

    private void LoadMinimum()
    {
        if (SelectedProduct is not { } product || SelectedWarehouse is not { } warehouse)
        {
            Minimum = null;
            return;
        }

        var key = new StockKey(warehouse.Id, product.Id);
        Minimum = balances.FirstOrDefault(row => row.Key == key)?.Minimum;
    }

    private void SaveDraft()
    {
        if (!CanManageCatalog) return;
        var saved = SelectedProduct is { } product
            ? catalog.UpdateProduct(product.Id, Draft.Sku, Draft.Barcode, Draft.Name, Draft.Unit)
            : catalog.CreateProduct(Draft.Sku, Draft.Barcode, Draft.Name, Draft.Unit);

        Status = "Товар сохранён.";
        LoadPage(offset, appliedQuery, appliedArchive, saved.Id);
        LoadBalances();
    }

    private void SetMinimum()
    {
        if (!CanSetMinimum || SelectedProduct is not { } product) return;
        if (SelectedWarehouse is not { } warehouse || Minimum is not { } quantity)
        {
            Error = "Выберите склад и введите минимальный остаток.";
            return;
        }

        catalog.SetMinimum(warehouse.Id, product.Id, quantity);
        Status = "Минимальный остаток сохранён.";
        LoadBalances();
    }

    private void LoadBalances()
    {
        balances = reports.Balances();
        Notify(nameof(VisibleBalances));
        LoadMinimum();
    }

    private void Run(Action action)
    {
        Error = Status = "";
        try { action(); }
        catch (WarehouseException exception)
        {
            Error = exception.Code switch
            {
                ErrorCode.Validation => "Проверьте заполнение полей и значение остатка.",
                ErrorCode.Duplicate => "Такой артикул или штрихкод уже используется.",
                ErrorCode.Forbidden => "У вашей роли нет прав для этого действия.",
                ErrorCode.InventoryLocked => "Товар участвует в незавершённой ревизии.",
                ErrorCode.InvalidState => "Проверьте доступность товара и склада. Единицу измерения товара с движениями нельзя изменить.",
                ErrorCode.NotFound => "Запись не найдена. Обновите список и повторите действие.",
                ErrorCode.Concurrency => "Данные изменились. Повторите действие.",
                _ => "Не удалось выполнить действие."
            };
        }
        catch (Exception exception) when (exception is DbException or IOException or TimeoutException)
        {
            Error = "Не удалось получить данные из хранилища.";
        }
    }
}

public sealed class ProductDraft
{
    public string Sku { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
}
