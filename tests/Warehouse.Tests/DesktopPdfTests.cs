using QuestPDF.Infrastructure;
using Warehouse.Application;
using Warehouse.Desktop.Reports;

namespace Warehouse.Tests;

public sealed class DesktopPdfTests
{
    [Fact]
    public void DeliveryUsesOriginalPostedAmountsAfterReversal()
    {
        var world = new World();
        world.Catalog.UpdateProduct(world.P.Id, "00123", world.P.Barcode, "Лента упаковочная", "шт.");
        var document = world.Receipt(2.5m, 3m);
        world.Documents.Reverse(document.Id);
        var forms = new PrintFormService(world.Work, world.Identity, world.Clock);

        var report = forms.Delivery(document.Id);

        Assert.Equal(new[] { "1", "00123", "Лента упаковочная", "шт.", "2.5", "3", "7.5" }, Assert.Single(report.Rows));
        Assert.Equal("7.5", report.Totals![6]);
        Assert.Contains(report.Details, detail => detail.Contains("Сторнирован"));
        AssertPdf(new PdfReportExporter(LicenseType.Community).Export(report, ExportFormat.Pdf));
    }

    [Fact]
    public void InventoryFormUsesActualPostedValuesAndSeparatedDiscrepancies()
    {
        var world = new World();
        world.Receipt(4m, 2.5m);
        var inventory = world.Inventories.Start(world.A.Id, [world.P.Id, world.Q.Id]);
        world.Inventories.Count(inventory.Id, world.P.Id, 2.5m);
        world.Inventories.Count(inventory.Id, world.Q.Id, 2.5m, 4.5m);
        world.Inventories.Complete(inventory.Id);
        var forms = new PrintFormService(world.Work, world.Identity, world.Clock);

        var report = forms.Reconciliation(inventory.Id);

        Assert.Equal("1.5", report.Rows[0][8]);
        Assert.Equal("3.75", report.Rows[0][9]);
        Assert.Equal("2.5", report.Rows[1][6]);
        Assert.Equal("11.25", report.Rows[1][7]);
        Assert.Equal("11.25", report.Totals![7]);
        Assert.Equal("3.75", report.Totals[9]);
        AssertPdf(new PdfReportExporter(LicenseType.Community).Export(report, ExportFormat.Pdf));
    }

    [Fact]
    public void PdfExporterGeneratesEmptyAndLongCyrillicReports()
    {
        var pdf = new PdfReportExporter(LicenseType.Community);
        var empty = new ReportTable("Остатки", ["Наименование", "Количество"], []) { NumericColumns = [1] };
        IReadOnlyList<string>[] rows = Enumerable.Range(1, 140)
            .Select(index => (IReadOnlyList<string>)new[] { $"Товар {index} — Ёж", "2.5" }).ToArray();

        AssertPdf(pdf.Export(empty, ExportFormat.Pdf));
        AssertPdf(pdf.Export(empty with { Rows = rows }, ExportFormat.Pdf));
    }

    private static void AssertPdf(ReportFile file)
    {
        Assert.Equal(PdfReportExporter.ContentType, file.ContentType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(file.Content.Span[..5]));
    }
}
