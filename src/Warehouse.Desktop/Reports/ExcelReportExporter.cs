using System.Globalization;
using ClosedXML.Excel;
using Warehouse.Application;

namespace Warehouse.Desktop.Reports;

public sealed class ExcelReportExporter : IReportExporter
{
    private const int HeaderRow = 3;
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public ReportFile Export(ReportTable report, ExportFormat format)
    {
        if (format != ExportFormat.Excel) throw new ArgumentOutOfRangeException(nameof(format));
        if (report.Columns.Count == 0 || report.Rows.Count > XLHelper.MaxRowNumber - HeaderRow)
            throw new InvalidDataException("Таблица не помещается на лист Excel.");

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Отчёт");
        sheet.Cell(1, 1).Value = report.Title;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16);
        var headings = sheet.Range(HeaderRow, 1, HeaderRow, report.Columns.Count);
        headings.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF2F7");
        headings.Style.Font.Bold = true;
        headings.Style.Alignment.WrapText = true;
        for (var column = 0; column < report.Columns.Count; column++)
            sheet.Cell(HeaderRow, column + 1).Value = report.Columns[column];

        var numeric = report.NumericColumns.ToHashSet();
        for (var index = 0; index < report.Rows.Count; index++)
        {
            var row = report.Rows[index];
            if (row.Count != report.Columns.Count) throw new InvalidDataException("Число ячеек не совпадает с заголовком.");
            for (var column = 0; column < row.Count; column++)
            {
                var value = row[column];
                if (value.Length == 0) continue;
                var cell = sheet.Cell(HeaderRow + index + 1, column + 1);
                if (numeric.Contains(column))
                {
                    if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number))
                        throw new InvalidDataException("Некорректное число в столбце отчёта.");
                    cell.Value = number;
                }
                else
                {
                    cell.Value = value;
                }
            }
        }

        var last = report.Rows.Count + HeaderRow;
        if (last > HeaderRow) sheet.Range(HeaderRow, 1, last, report.Columns.Count).SetAutoFilter();
        foreach (var column in numeric)
            sheet.Column(column + 1).Style.NumberFormat.Format = "0.############################";
        for (var column = 1; column <= report.Columns.Count; column++)
            sheet.Column(column).AdjustToContents(HeaderRow, Math.Min(last, HeaderRow + 120), 14, 45);
        sheet.SheetView.FreezeRows(HeaderRow);
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return new ReportFile("Складской отчёт.xlsx", ContentType, output.ToArray());
    }
}
