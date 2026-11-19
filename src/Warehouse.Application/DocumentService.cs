using Warehouse.Domain;

namespace Warehouse.Application;

public sealed class DocumentService(IUnitOfWork work, ICurrentUser user, IClock clock) : ServiceBase(work, user, clock)
{
    public StockDocument Create(DocumentKind kind, Guid warehouseId, IEnumerable<DocumentLine> lines, Guid? destinationId = null)
    {
        var input = Snapshot(lines);
        return Run(Permission.ManageDocuments, s =>
        {
            Rules.Require(kind is DocumentKind.Receipt or DocumentKind.Issue or DocumentKind.Transfer, ErrorCode.Forbidden,
                "Inventory acts can only be created by inventory completion.");
            var document = StockDocument.Draft(Guid.NewGuid(), kind, warehouseId, destinationId, input(), Clock.UtcNow);
            ValidateReferences(s, document);
            s.Documents.Add(document);
            Audit(s, "DocumentCreated", document.Id);
            return document;
        });
    }

    public StockDocument Edit(Guid id, IEnumerable<DocumentLine> lines)
    {
        var input = Snapshot(lines);
        return Run(Permission.ManageDocuments, s =>
        {
            var document = Get(s.Documents, id).Edit(input());
            ValidateReferences(s, document);
            s.Documents.Update(document);
            Audit(s, "DocumentEdited", id);
            return document;
        });
    }

    public StockDocument GetDocument(Guid id) => Run(Permission.Read, s => Get(s.Documents, id));
    public IReadOnlyList<StockDocument> List() => Run(Permission.Read, s => s.Documents.All());

    public StockDocument Post(Guid id) => Run(Permission.ManageDocuments, s =>
    {
        var document = Get(s.Documents, id);
        Rules.Require(document.InventoryId == null, ErrorCode.Forbidden, "Inventory acts are managed by inventory completion.");
        var posted = PostInSession(s, document, Clock.UtcNow);
        Audit(s, "DocumentPosted", id);
        return posted;
    });

    public StockDocument Reverse(Guid id) => Run(Permission.ReverseDocuments, s =>
    {
        var document = Get(s.Documents, id);
        var now = Clock.UtcNow;
        var reversed = document.Reverse(now);
        ValidateReferences(s, document);
        var originals = s.Movements.All().Where(x => x.DocumentId == id && x.ReversesMovementId == null).ToArray();
        Rules.Require(originals.Length > 0, ErrorCode.InvalidState, "The posted document has no movements.");
        foreach (var movement in originals)
        {
            Unlocked(s, movement.Key);
            var inverse = new StockMovement(Guid.NewGuid(), id, movement.Key, -movement.Quantity, -movement.Value, now, movement.Id);
            s.Stock.Put(Balance(s, movement.Key).Apply(inverse.Quantity, inverse.Value));
            s.Movements.Add(inverse);
        }
        s.Documents.Update(reversed);
        Audit(s, "DocumentReversed", id);
        return reversed;
    });

    internal static StockDocument PostInSession(IWarehouseSession s, StockDocument document, DateTimeOffset now, Guid? inventoryId = null)
    {
        ValidateReferences(s, document);
        Rules.Require(document.InventoryId == inventoryId, ErrorCode.Forbidden, "Invalid inventory ownership.");
        var posted = document.Post(s.NextDocumentNumber(), now);
        foreach (var line in document.Lines)
        {
            var source = new StockKey(document.WarehouseId, line.ProductId);
            Unlocked(s, source, inventoryId);
            var balance = Balance(s, source);
            if (document.Kind is DocumentKind.Receipt or DocumentKind.InventorySurplus)
            {
                var value = Rules.Calculate(() => line.Quantity * line.UnitCost!.Value);
                Move(s, document.Id, balance, line.Quantity, value, now);
            }
            else
            {
                var value = balance.IssueValue(line.Quantity);
                Move(s, document.Id, balance, -line.Quantity, -value, now);
                if (document.Kind == DocumentKind.Transfer)
                {
                    var target = new StockKey(document.DestinationId!.Value, line.ProductId);
                    Unlocked(s, target, inventoryId);
                    Move(s, document.Id, Balance(s, target), line.Quantity, value, now);
                }
            }
        }
        s.Documents.Update(posted);
        return posted;
    }

    private static void Move(IWarehouseSession s, Guid documentId, StockBalance balance, decimal quantity, decimal value, DateTimeOffset now)
    {
        s.Stock.Put(balance.Apply(quantity, value));
        s.Movements.Add(new StockMovement(Guid.NewGuid(), documentId, balance.Key, quantity, value, now));
    }

    private static void ValidateReferences(IWarehouseSession s, StockDocument document)
    {
        ActiveWarehouse(s, document.WarehouseId);
        if (document.DestinationId is { } destination) ActiveWarehouse(s, destination);
        foreach (var line in document.Lines) ActiveProduct(s, line.ProductId);
    }
}
