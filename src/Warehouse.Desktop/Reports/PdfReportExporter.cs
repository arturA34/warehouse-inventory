using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Warehouse.Application;

namespace Warehouse.Desktop.Reports;

public sealed class PdfReportExporter(LicenseType license) : IReportExporter
{
    public const string ContentType = "application/pdf";

    public ReportFile Export(ReportTable report, ExportFormat format)
    {
        if (format != ExportFormat.Pdf) throw new ArgumentOutOfRangeException(nameof(format));
        if (report.Columns.Count == 0 || report.Rows.Any(row => row.Count != report.Columns.Count) ||
            report.Totals is { } totals && totals.Count != report.Columns.Count)
            throw new InvalidDataException("Строки отчёта должны совпадать с заголовками.");

        QuestPDF.Settings.License ??= license;
        var numeric = report.NumericColumns.ToHashSet();
        var pdf = Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(26);
            page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(9).FontColor("#182637"));
            page.Header().PaddingBottom(10).Column(header =>
            {
                header.Spacing(4);
                if (report.DraftPrintForm)
                    header.Item().Text("ПРОЕКТ ДЛЯ СОГЛАСОВАНИЯ").FontSize(9).SemiBold().FontColor("#A32C35");
                header.Item().Text(report.Title).FontSize(18).SemiBold();
            });
            page.Content().Column(content =>
            {
                content.Spacing(8);
                foreach (var detail in report.Details) content.Item().Text(detail);
                content.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        for (var i = 0; i < report.Columns.Count; i++)
                            columns.RelativeColumn(numeric.Contains(i) ? (i == 0 ? 0.55f : 1f) : 1.5f);
                    });
                    table.Header(header =>
                    {
                        foreach (var heading in report.Columns)
                            header.Cell().Background("#EDF2F7").Padding(5).Text(heading).SemiBold();
                    });
                    foreach (var row in report.Rows)
                    {
                        for (var i = 0; i < row.Count; i++)
                        {
                            var cell = table.Cell().BorderBottom(0.5f).BorderColor("#DCE3EC").Padding(5);
                            if (numeric.Contains(i)) cell = cell.AlignRight();
                            cell.Text(row[i]);
                        }
                    }
                    if (report.Totals is { } total)
                    {
                        for (var i = 0; i < total.Count; i++)
                        {
                            var cell = table.Cell().Background("#EDF2F7").Padding(5);
                            if (numeric.Contains(i) && i != 0) cell = cell.AlignRight();
                            cell.Text(total[i]).SemiBold();
                        }
                    }
                });
                if (report.Rows.Count == 0) content.Item().Text("Нет данных за выбранный период или по заданному фильтру.");
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span("Страница ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();

        var name = report.DraftPrintForm ? "Проект печатной формы.pdf" : "Складской отчёт.pdf";
        return new ReportFile(name, ContentType, pdf);
    }
}

internal sealed class DesktopReportExporter(IReportExporter excel, IReportExporter pdf) : IReportExporter
{
    public ReportFile Export(ReportTable report, ExportFormat format) => format switch
    {
        ExportFormat.Excel => excel.Export(report, format),
        ExportFormat.Pdf => pdf.Export(report, format),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
