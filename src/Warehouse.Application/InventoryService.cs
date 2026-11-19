using Warehouse.Domain;

namespace Warehouse.Application;

public sealed class InventoryService(IUnitOfWork work, ICurrentUser user, IClock clock) : ServiceBase(work, user, clock)
{
    public InventorySession Start(Guid warehouseId, IEnumerable<Guid> productIds)
    {
        var input = Snapshot(productIds);
        return Run(Permission.Inventory, s =>
        {
            ActiveWarehouse(s, warehouseId);
            var ids = input();
            var lines = ids.Select(id =>
            {
                ActiveProduct(s, id);
                var key = new StockKey(warehouseId, id);
                Unlocked(s, key);
                var stock = Balance(s, key);
                return new InventoryLine(id, stock.Quantity, stock.Value);
            }).ToArray();
            var inventory = InventorySession.Start(Guid.NewGuid(), warehouseId, lines, Clock.UtcNow);
            s.Inventories.Add(inventory);
            Audit(s, "InventoryStarted", inventory.Id);
            return inventory;
        });
    }

    public InventorySession Count(Guid id, Guid productId, decimal quantity, decimal? surplusUnitCost = null) => Run(Permission.Inventory, s =>
    {
        var inventory = Get(s.Inventories, id).Count(productId, quantity, surplusUnitCost);
        s.Inventories.Update(inventory);
        Audit(s, "InventoryCounted", id);
        return inventory;
    });

    public InventorySession Scan(Guid id, string barcode, decimal increment = 1) => Run(Permission.Inventory, s =>
    {
        var product = s.Products.ByBarcode(Rules.Text(barcode)) ?? throw new WarehouseException(ErrorCode.NotFound, "Unknown barcode.");
        var inventory = Get(s.Inventories, id).Scan(product.Id, increment);
        s.Inventories.Update(inventory);
        Audit(s, "InventoryScanned", id);
        return inventory;
    });

    public InventorySession Complete(Guid id) => Run(Permission.Inventory, s =>
    {
        var inventory = Get(s.Inventories, id);
        Rules.Require(inventory.Status == InventoryStatus.Active && inventory.Lines.All(x => x.ActualQuantity != null),
            ErrorCode.InvalidState, "Inventory must be active and fully counted.");
        var now = Clock.UtcNow;
        var surplus = new List<DocumentLine>();
        var shortage = new List<DocumentLine>();
        foreach (var line in inventory.Lines)
        {
            var difference = line.Difference!.Value;
            if (difference > 0)
                surplus.Add(new DocumentLine(line.ProductId, difference, line.SurplusPrice()));
            if (difference < 0)
                shortage.Add(new DocumentLine(line.ProductId, -difference));
        }

        var documents = new List<Guid>();
        void PostAct(DocumentKind kind, List<DocumentLine> lines)
        {
            if (lines.Count == 0) return;
            var draft = StockDocument.Draft(Guid.NewGuid(), kind, inventory.WarehouseId, null, lines, now, id);
            s.Documents.Add(draft);
            DocumentService.PostInSession(s, draft, now, id);
            Audit(s, "InventoryActPosted", draft.Id);
            documents.Add(draft.Id);
        }

        PostAct(DocumentKind.InventorySurplus, surplus);
        PostAct(DocumentKind.InventoryShortage, shortage);
        var completed = inventory.Complete(documents, now);
        s.Inventories.Update(completed);
        Audit(s, "InventoryCompleted", id);
        return completed;
    });

    public InventorySession Cancel(Guid id) => Run(Permission.Inventory, s =>
    {
        var inventory = Get(s.Inventories, id).Cancel(Clock.UtcNow);
        s.Inventories.Update(inventory);
        Audit(s, "InventoryCancelled", id);
        return inventory;
    });

    public InventorySession GetInventory(Guid id) => Run(Permission.Read, s => Get(s.Inventories, id));
    public IReadOnlyList<InventorySession> List() => Run(Permission.Read, s => s.Inventories.All());
}
