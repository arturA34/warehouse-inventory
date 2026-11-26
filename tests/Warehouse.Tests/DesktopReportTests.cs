using ClosedXML.Excel;
using Warehouse.Application;
using Warehouse.Desktop.Reports;
using Warehouse.Desktop.ViewModels;

namespace Warehouse.Tests;

public sealed class DesktopReportTests
{
    [Theory]
    [InlineData(ReportKind.Balances, 5)]
    [InlineData(ReportKind.Movements, 6)]
    [InlineData(ReportKind.Turnover, 6)]
    [InlineData(ReportKind.Reconciliation, 3)]
    public async Task WorkbookUsesThePreviewAndPreservesNumericCells(ReportKind kind, int valueColumn)
    {
        var world = new World();
        world.Catalog.UpdateProduct(world.P.Id, "00123", world.P.Barcode, "Коробка Ё", "шт.");
        world.Receipt(2.5m, 3m);
        var audit = world.Inventories.Start(world.A.Id, [world.P.Id]);
        var model = new ReportsViewModel(world.Reports, world.Catalog, world.Inventories, new ExcelReportExporter());
        model.RefreshSources();
        model.SelectedReport = model.Choices.Single(choice => choice.Kind == kind);
        model.From = model.Until = world.Clock.UtcNow.ToLocalTime();
        model.SelectedInventory = Assert.Single(model.Inventories, inventory => inventory.Id == audit.Id);
        await model.GenerateAsync();
        var snapshot = Assert.IsType<ReportTable>(model.Table);
        Assert.Contains(Assert.Single(snapshot.Rows), cell => cell.Contains("Коробка Ё"));

        if (kind == ReportKind.Reconciliation) world.Inventories.Count(audit.Id, world.P.Id, 0);
        var file = await CaptureExcel(model);
        using var book = new XLWorkbook(new MemoryStream(file.Content.ToArray()));
        var sheet = book.Worksheet(1);
        Assert.Equal(snapshot.Title, sheet.Cell(1, 1).GetString());
        Assert.Equal(snapshot.Columns[valueColumn - 1], sheet.Cell(3, valueColumn).GetString());
        Assert.Equal(XLDataType.Number, sheet.Cell(4, valueColumn).DataType);
        Assert.Equal(7.5, sheet.Cell(4, valueColumn).GetDouble());
        if (kind == ReportKind.Balances) Assert.Equal("00123", sheet.Cell(4, 2).GetString());
        if (kind == ReportKind.Reconciliation) Assert.True(sheet.Cell(4, 4).IsEmpty());
    }

    [Fact]
    public async Task EmptySelectionStillExportsHeadings()
    {
        var world = new World();
        var model = new ReportsViewModel(world.Reports, world.Catalog, world.Inventories, new ExcelReportExporter());
        await model.GenerateAsync();
        Assert.Empty(model.Table!.Rows);
        var file = await CaptureExcel(model);
        using var book = new XLWorkbook(new MemoryStream(file.Content.ToArray()));
        Assert.Equal("Склад", book.Worksheet(1).Cell(3, 1).GetString());
        Assert.True(book.Worksheet(1).Cell(4, 1).IsEmpty());
    }

    private static async Task<ReportFile> CaptureExcel(ReportsViewModel model)
    {
        ReportFile? output = null;
        await model.SaveAsync(ExportFormat.Excel, file =>
        {
            output = file;
            return Task.FromResult(true);
        });
        return Assert.IsType<ReportFile>(output);
    }
}
