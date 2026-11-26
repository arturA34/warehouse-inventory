using System.Globalization;
using Warehouse.Domain;

namespace Warehouse.Application;

public sealed class PrintFormService(IUnitOfWork work, ICurrentUser user, IClock clock) : ServiceBase(work, user, clock)
{
    public static bool CanPrintDelivery(StockDocument document) =>
        document.Status != DocumentStatus.Draft &&
        document.Kind is DocumentKind.Receipt or DocumentKind.Issue or DocumentKind.Transfer;

    public ReportTable Delivery(Guid id) => Run(Permission.Read, s =>
    {
        var document = Get(s.Documents, id);
        Rules.Require(CanPrintDelivery(document), ErrorCode.InvalidState, "Выберите проведённый складской документ.");
        var movements = s.Movements.All()
            .Where(x => x.DocumentId == id && x.Key.WarehouseId == document.WarehouseId && x.ReversesMovementId is null)
            .ToDictionary(x => x.Key.ProductId);
        var amounts = new List<decimal>(document.Lines.Count);
        var rows = document.Lines.Select((line, index) =>
        {
            var product = Get(s.Products, line.ProductId);
            Rules.Require(movements.ContainsKey(product.Id), ErrorCode.InvalidState, "У строки документа отсутствует движение.");
            var amount = Math.Abs(movements[product.Id].Value);
            amounts.Add(amount);
            var price = line.UnitCost ?? Rules.Calculate(() => amount / line.Quantity);
            return Row((index + 1).ToString(CultureInfo.InvariantCulture), product.Sku, product.Name, product.Unit,
                Number(line.Quantity), Number(price), Number(amount));
        }).ToArray();

        var source = Get(s.Warehouses, document.WarehouseId).Name;
        var route = document.DestinationId is { } target ? $"{source} → {Get(s.Warehouses, target).Name}" : source;
        var state = document.Status == DocumentStatus.Reversed ? "Сторнирован" : "Проведён";
        return new ReportTable("Товарная накладная · ТОРГ-12",
            ["№", "Артикул", "Товар", "Ед.", "Количество", "Цена", "Сумма"], rows)
        {
            NumericColumns = [0, 4, 5, 6],
            Details = [$"Складской документ № {document.Number} от {document.PostedAt!.Value.ToLocalTime():dd.MM.yyyy} · {state}",
                $"Склад / маршрут · {route}", "Поставщик __________________    Получатель __________________",
                "Основание __________________    НДС __________________",
                "Отпустил __________________    Принял __________________"],
            Totals = Row("Итого", "", "", "", "", "", Number(Rules.Sum(amounts))),
            DraftPrintForm = true
        };
    });

    public ReportTable Reconciliation(Guid id) => Run(Permission.Read, s =>
    {
        var inventory = Get(s.Inventories, id);
        Rules.Require(inventory.Status == InventoryStatus.Completed, ErrorCode.InvalidState, "Выберите завершённую ревизию.");
        var adjustments = inventory.AdjustmentDocumentIds.ToHashSet();
        var values = s.Movements.All().Where(x => adjustments.Contains(x.DocumentId) && x.ReversesMovementId is null)
            .ToDictionary(x => x.Key.ProductId, x => Math.Abs(x.Value));
        var excessAmounts = new List<decimal>();
        var shortageAmounts = new List<decimal>();
        var rows = inventory.Lines.Select((line, index) =>
        {
            var product = Get(s.Products, line.ProductId);
            var difference = line.Difference!.Value;
            Rules.Require(difference == 0 || values.ContainsKey(product.Id), ErrorCode.InvalidState,
                "У расхождения отсутствует движение по акту.");
            var amount = values.GetValueOrDefault(product.Id);
            if (difference > 0) excessAmounts.Add(amount);
            if (difference < 0) shortageAmounts.Add(amount);
            return Row((index + 1).ToString(CultureInfo.InvariantCulture), product.Sku, product.Name, product.Unit,
                Number(line.BookQuantity), Number(line.ActualQuantity!.Value),
                difference > 0 ? Number(difference) : "", difference > 0 ? Number(amount) : "",
                difference < 0 ? Number(-difference) : "", difference < 0 ? Number(amount) : "");
        }).ToArray();

        return new ReportTable("Сличительная ведомость · ИНВ-19",
            ["№", "Артикул", "Товар", "Ед.", "Учёт", "Факт", "Излишек", "Сумма излишка", "Недостача", "Сумма недостачи"], rows)
        {
            NumericColumns = [0, 4, 5, 6, 7, 8, 9],
            Details = [$"Склад · {Get(s.Warehouses, inventory.WarehouseId).Name}",
                $"Ревизия от {inventory.StartedAt.ToLocalTime():dd.MM.yyyy} · завершена {inventory.FinishedAt!.Value.ToLocalTime():dd.MM.yyyy}",
                $"Идентификатор ревизии · {inventory.Id}",
                "Организация __________________    Основание __________________",
                "Комиссия __________________    Материально ответственное лицо __________________"],
            Totals = Row("Итого", "", "", "", "", "", "", Number(Rules.Sum(excessAmounts)), "", Number(Rules.Sum(shortageAmounts))),
            DraftPrintForm = true
        };
    });

    private static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static IReadOnlyList<string> Row(params string[] cells) => Array.AsReadOnly(cells);
}
