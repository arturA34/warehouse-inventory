namespace Warehouse.Tests;

public sealed class ReportTests
{
    [Fact]
    public void Minimum_alert_is_strictly_below_and_includes_positions_without_stock_rows()
    {
        var w = new World();
        Assert.Empty(w.Reports.Balances());
        w.Catalog.SetMinimum(w.A.Id, w.P.Id, 2);
        var empty = Assert.Single(w.Reports.Balances(belowMinimumOnly: true));
        Assert.Equal((0m, 0m, 0m, 2m, true), (empty.Quantity, empty.Value, empty.AverageCost, empty.Minimum, empty.BelowMinimum));
        Assert.Equal(("P", "Product P", "A"), (empty.Sku, empty.ProductName, empty.WarehouseName));
        w.Receipt(2, 3);
        Assert.Empty(w.Reports.Balances(belowMinimumOnly: true));
        w.Issue(0.5m);
        Assert.Equal(1.5m, Assert.Single(w.Reports.Balances(belowMinimumOnly: true)).Quantity);
        w.Catalog.SetMinimum(w.A.Id, w.P.Id, 0);
        Assert.Empty(w.Reports.Balances(belowMinimumOnly: true));
        w.Receipt(1, 5, w.B.Id);
        Assert.Equal(2, w.Reports.Balances().Count);
        Assert.Equal(w.B.Id, Assert.Single(w.Reports.Balances(w.B.Id)).Key.WarehouseId);
        Check.Error(ErrorCode.NotFound, () => w.Reports.Balances(Guid.NewGuid()));
    }

    [Fact]
    public void Movement_period_is_inclusive_and_filters_warehouse_and_product()
    {
        var w = new World();
        var from = w.Clock.UtcNow;
        w.Receipt(2, 3);
        w.Receipt(1, 1, productId: w.Q.Id);
        w.Clock.UtcNow = from.AddSeconds(1);
        w.Receipt(1, 4, w.B.Id);
        var to = w.Clock.UtcNow;
        w.Clock.UtcNow = to.AddTicks(1);
        w.Issue(1);
        Assert.Equal(3, w.Reports.Movements(from, to).Count);
        Assert.Equal(2, w.Reports.Movements(from, from).Count);
        Assert.Single(w.Reports.Movements(to, to));
        Assert.Equal(2, w.Reports.Movements(from, to, w.A.Id).Count);
        Assert.Equal(2, w.Reports.Movements(from, to, productId: w.P.Id).Count);
        Assert.Single(w.Reports.Movements(from, to, w.A.Id, w.P.Id));
        Assert.Empty(w.Reports.Movements(to.AddSeconds(1), to.AddSeconds(2)));
    }

    [Fact]
    public void Turnover_has_known_opening_income_expense_and_closing_values_at_boundaries()
    {
        var w = new World();
        w.Receipt(10, 10);
        w.Clock.UtcNow = w.Clock.UtcNow.AddSeconds(1);
        var from = w.Clock.UtcNow;
        w.Receipt(10, 20);
        w.Clock.UtcNow = from.AddSeconds(1);
        var to = w.Clock.UtcNow;
        w.Issue(5);
        w.Clock.UtcNow = to.AddTicks(1);
        w.Receipt(1, 1000); // Этот приход уже за границей периода.
        var row = Assert.Single(w.Reports.Turnover(from, to, w.A.Id, w.P.Id));
        Assert.Equal((10m, 100m), (row.OpeningQuantity, row.OpeningValue));
        Assert.Equal((10m, 200m), (row.ReceivedQuantity, row.ReceivedValue));
        Assert.Equal((5m, 75m), (row.IssuedQuantity, row.IssuedValue));
        Assert.Equal((15m, 225m), (row.ClosingQuantity, row.ClosingValue));
        var emptyPeriod = Assert.Single(w.Reports.Turnover(to.AddDays(1), to.AddDays(2)));
        Assert.Equal((16m, 1225m, 0m, 0m), (emptyPeriod.OpeningQuantity, emptyPeriod.OpeningValue, emptyPeriod.ReceivedQuantity, emptyPeriod.IssuedQuantity));
        Assert.Empty(w.Reports.Turnover(from, to, productId: w.Q.Id));
    }

    [Fact]
    public void Reversed_documents_remain_in_reports_and_net_to_zero()
    {
        var w = new World();
        var from = w.Clock.UtcNow;
        var receipt = w.Receipt(2, 3);
        w.Clock.UtcNow = from.AddSeconds(1);
        w.Documents.Reverse(receipt.Id);
        var movements = w.Reports.Movements(from, w.Clock.UtcNow);
        Assert.Equal(2, movements.Count);
        Assert.Equal(0, movements.Sum(x => x.Quantity));
        Assert.Equal(0, movements.Sum(x => x.Value));
        var row = Assert.Single(w.Reports.Turnover(from, w.Clock.UtcNow));
        Assert.Equal((2m, 6m, 2m, 6m, 0m, 0m), (row.ReceivedQuantity, row.ReceivedValue, row.IssuedQuantity, row.IssuedValue, row.ClosingQuantity, row.ClosingValue));
    }

    [Fact]
    public void Report_queries_reject_invalid_ranges_and_missing_references()
    {
        var w = new World();
        var now = w.Clock.UtcNow;
        var missing = Guid.NewGuid();
        Check.Error(ErrorCode.Validation, () => w.Reports.Movements(now, now.AddTicks(-1)));
        Check.Error(ErrorCode.Validation, () => w.Reports.Turnover(now, now.AddTicks(-1)));
        Check.Error(ErrorCode.NotFound, () => w.Reports.Movements(now, now, missing));
        Check.Error(ErrorCode.NotFound, () => w.Reports.Movements(now, now, productId: missing));
        Check.Error(ErrorCode.NotFound, () => w.Reports.Turnover(now, now, missing));
        Check.Error(ErrorCode.NotFound, () => w.Reports.Turnover(now, now, productId: missing));
        Check.Error(ErrorCode.NotFound, () => w.Reports.Reconciliation(missing));
        Assert.Empty(w.Reports.Turnover(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Empty(w.Reports.Movements(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Theory]
    [InlineData(ExportFormat.Excel)]
    [InlineData(ExportFormat.Pdf)]
    public void Each_export_passes_detached_expected_data_and_format_to_infrastructure(ExportFormat format)
    {
        var w = new World();
        w.Receipt(2.5m, 3);
        var now = w.Clock.UtcNow;
        var file = w.Reports.ExportBalances(format);
        Assert.Equal("test", file.Name);
        Assert.Equal("application/test", file.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, file.Content.ToArray());
        Assert.Equal(format, w.Exporter.LastFormat);
        Assert.Equal(new[] { "A", "P", "Product P", "2.5", "7.5", "3", "0" }, Assert.Single(w.Exporter.LastReport!.Rows));
        w.Reports.ExportMovements(format, now, now);
        var movement = Assert.Single(w.Exporter.LastReport!.Rows);
        Assert.Equal("2.5", movement[4]);
        Assert.Equal("7.5", movement[5]);
        Assert.Equal("", movement[6]);
        w.Reports.ExportTurnover(format, now, now);
        Assert.Equal(new[] { w.A.Id.ToString(), w.P.Id.ToString(), "0", "0", "2.5", "7.5", "0", "0", "2.5", "7.5" }, Assert.Single(w.Exporter.LastReport!.Rows));
        var inventory = w.Inventories.Start(w.A.Id, [w.P.Id]);
        w.Reports.ExportReconciliation(format, inventory.Id);
        Assert.Equal(new[] { w.P.Id.ToString(), "2.5", "7.5", "", "" }, Assert.Single(w.Exporter.LastReport!.Rows));
        w.Inventories.Count(inventory.Id, w.P.Id, 0);
        w.Reports.ExportReconciliation(format, inventory.Id);
        Assert.Equal(new[] { w.P.Id.ToString(), "2.5", "7.5", "0", "-2.5" }, Assert.Single(w.Exporter.LastReport!.Rows));
        Assert.Equal(5, w.Exporter.Calls);
    }

    [Fact]
    public void Export_rejects_unknown_format_without_calling_exporter()
    {
        var w = new World();
        var format = (ExportFormat)99;
        Check.Error(ErrorCode.Validation, () => w.Reports.ExportBalances(format));
        Check.Error(ErrorCode.Validation, () => w.Reports.ExportMovements(format, w.Clock.UtcNow, w.Clock.UtcNow));
        Check.Error(ErrorCode.Validation, () => w.Reports.ExportTurnover(format, w.Clock.UtcNow, w.Clock.UtcNow));
        Check.Error(ErrorCode.Validation, () => w.Reports.ExportReconciliation(format, Guid.NewGuid()));
        Assert.Equal(0, w.Exporter.Calls);
    }

    [Fact]
    public void Export_is_culture_independent_and_reversal_reference_is_preserved()
    {
        var w = new World();
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            var receipt = w.Receipt(0.5m, 2);
            w.Reports.ExportBalances(ExportFormat.Excel);
            Assert.Equal("0.5", Assert.Single(w.Exporter.LastReport!.Rows)[3]);
            w.Documents.Reverse(receipt.Id);
            w.Reports.ExportMovements(ExportFormat.Pdf, w.Clock.UtcNow, w.Clock.UtcNow);
            Assert.Single(w.Exporter.LastReport!.Rows, row => row[6] != "");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
}
