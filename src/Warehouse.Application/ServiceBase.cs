using Warehouse.Domain;

namespace Warehouse.Application;

public abstract class ServiceBase(IUnitOfWork unitOfWork, ICurrentUser currentUser, IClock clock)
{
    protected IClock Clock { get; } = clock;
    protected Guid? ActorId => currentUser.UserId;

    protected T Run<T>(Permission permission, Func<IWarehouseSession, T> operation) => Transaction(session =>
    {
        var actor = currentUser.UserId is { } id ? session.Users.Find(id) : null;
        Rules.Require(actor != null && actor.IsEnabled && !actor.IsLocked && AccessPolicy.Allows(actor.Role, permission),
            ErrorCode.Forbidden, "This operation is not permitted.");
        return operation(session);
    });

    protected T Transaction<T>(Func<IWarehouseSession, T> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return unitOfWork.Execute(operation); }
            catch (ConcurrencyConflictException) when (attempt < 2) { }
            catch (ConcurrencyConflictException) { throw new WarehouseException(ErrorCode.Concurrency, "The operation conflicted three times. Try again."); }
        }
    }

    protected void Audit(IWarehouseSession session, string action, Guid? entityId) =>
        session.Audit.Add(new AuditEntry(Guid.NewGuid(), ActorId, action, entityId, Clock.UtcNow));

    protected static T Get<T>(IReadRepository<T> repository, Guid id) where T : class =>
        repository.Find(Rules.Id(id)) ?? throw new WarehouseException(ErrorCode.NotFound, $"{typeof(T).Name} was not found.");

    protected static Product ActiveProduct(IWarehouseSession session, Guid id)
    {
        var product = Get(session.Products, id);
        Rules.Require(product.IsActive, ErrorCode.InvalidState, "The product is archived.");
        return product;
    }

    protected static WarehouseSite ActiveWarehouse(IWarehouseSession session, Guid id)
    {
        var warehouse = Get(session.Warehouses, id);
        Rules.Require(warehouse.IsActive, ErrorCode.InvalidState, "The warehouse is archived.");
        return warehouse;
    }

    protected static StockBalance Balance(IWarehouseSession session, StockKey key) => session.Stock.Find(key) ?? new StockBalance(key);

    // Читаем входные строки после проверки прав. При повторе берём ту же копию.
    protected static Func<T[]> Snapshot<T>(IEnumerable<T> input)
    {
        T[]? cache = null;
        return () =>
        {
            Rules.Require(input != null, ErrorCode.Validation, "Input collection is required.");
            return cache ??= input!.ToArray();
        };
    }

    protected static void Unlocked(IWarehouseSession session, StockKey key, Guid? inventoryId = null) =>
        Rules.Require(!session.Inventories.All().Any(x => x.Status == InventoryStatus.Active && x.Id != inventoryId &&
            x.WarehouseId == key.WarehouseId && x.Lines.Any(y => y.ProductId == key.ProductId)),
            ErrorCode.InventoryLocked, "The product is locked by an active inventory.");
}
