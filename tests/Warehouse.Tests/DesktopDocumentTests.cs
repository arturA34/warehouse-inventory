using Warehouse.Desktop.ViewModels;
using Warehouse.Domain;

namespace Warehouse.Tests;

public sealed class DesktopDocumentTests
{
    [Fact]
    public void TransferValidatesWarehousesAndRefreshesHistoryOnPostAndReversal()
    {
        var world = new World();
        world.Receipt(5m, 2m);
        var balanceRefreshes = 0;
        var model = new DocumentsViewModel(world.Documents, world.Catalog, world.Reports, true, true, () => balanceRefreshes++);
        model.Kind = model.Kinds.Single(kind => kind.Kind == DocumentKind.Transfer);
        model.Source = model.Warehouses.Single(site => site.Id == world.A.Id);
        model.Destination = model.Source;
        model.Picker.Selected = world.P;
        model.AddLine.Execute(null);
        var row = Assert.Single(model.Lines);
        row.Quantity = 2m;

        model.Save.Execute(null);
        Assert.Equal("Выберите два разных действующих склада.", model.Error);
        Assert.Same(row, Assert.Single(model.Lines));

        model.Destination = model.Warehouses.Single(site => site.Id == world.B.Id);
        model.Save.Execute(null);
        Assert.True(model.CanPost);

        model.Post.Execute(null);
        Assert.False(model.CanEdit);
        Assert.True(model.CanReverse);
        Assert.NotNull(model.Selected?.Document.Number);
        Assert.Equal(2, model.History.Count);
        Assert.Equal(2m, world.Stock(world.B.Id).Quantity);

        model.Reverse.Execute(null);
        Assert.Equal(DocumentStatus.Reversed, model.Selected?.Document.Status);
        Assert.Equal(4, model.History.Count);
        Assert.Equal(0m, world.Stock(world.B.Id).Quantity);
        Assert.Equal(2, balanceRefreshes);
    }

    [Fact]
    public void PostingAnEditedDraftKeepsUserInputAndStoredDraftOnStockFailure()
    {
        var world = new World();
        world.Receipt(3m, 2m);
        var draft = world.Documents.Create(DocumentKind.Issue, world.A.Id, [new DocumentLine(world.P.Id, 1m)]);
        var model = new DocumentsViewModel(world.Documents, world.Catalog, world.Reports, true, false, () => { });
        model.Selected = model.Entries.Single(item => item.Document.Id == draft.Id);
        var row = Assert.Single(model.Lines);
        row.Quantity = 4m;

        model.Post.Execute(null);

        Assert.Equal("На складе недостаточно товара.", model.Error);
        Assert.Same(row, Assert.Single(model.Lines));
        Assert.Equal(4m, row.Quantity);
        Assert.Equal(1m, Assert.Single(world.Documents.GetDocument(draft.Id).Lines).Quantity);
        Assert.Equal(3m, world.Stock().Quantity);

        row.Quantity = 2m;
        model.Post.Execute(null);
        Assert.Equal(1m, world.Stock().Quantity);
        Assert.Equal(-2m, Assert.Single(model.History).Quantity);
        Assert.False(model.CanReverse);
    }
}
