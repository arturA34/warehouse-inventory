using System.Windows.Input;
using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public sealed class ProductPickerViewModel : OperationViewModel
{
    private const int PageSize = 40;
    private readonly CatalogService catalog;
    private string appliedQuery = "";
    private int offset;
    private int total;
    private IReadOnlyList<Product> products = [];
    private Product? selected;

    internal ProductPickerViewModel(CatalogService catalog)
    {
        this.catalog = catalog;
        Search = new Command(() => Run(() => Load(Query?.Trim() ?? "", 0)));
        Previous = new Command(() => Run(() => { if (CanGoBack) Load(appliedQuery, offset - PageSize); }));
        Next = new Command(() => Run(() => { if (CanGoForward) Load(appliedQuery, offset + PageSize); }));
        Search.Execute(null);
    }

    public string? Query { get; set; }
    public IReadOnlyList<Product> Products { get => products; private set => Set(ref products, value); }
    public Product? Selected { get => selected; set => Set(ref selected, value); }
    public bool CanGoBack => offset > 0;
    public bool CanGoForward => total - offset > PageSize;
    public string Range => total == 0 ? "Нет товаров" : $"{offset + 1}–{offset + Products.Count} из {total}";
    public ICommand Search { get; }
    public ICommand Previous { get; }
    public ICommand Next { get; }

    private void Load(string query, int skip)
    {
        var page = catalog.Search(query, skip: skip, take: PageSize);
        if (skip > 0 && skip >= page.Total)
        {
            skip = page.Total == 0 ? 0 : (page.Total - 1) / PageSize * PageSize;
            page = catalog.Search(query, skip: skip, take: PageSize);
        }

        appliedQuery = query;
        offset = skip;
        total = page.Total;
        Products = page.Items;
        Selected = null;
        Notify(nameof(CanGoBack));
        Notify(nameof(CanGoForward));
        Notify(nameof(Range));
    }
}
