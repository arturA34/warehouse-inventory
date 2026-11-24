using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.Storage;

internal sealed class MemoryUnitOfWork : IUnitOfWork
{
    private readonly object gate = new();
    private MemoryState committed = new();

    public T Execute<T>(Func<IWarehouseSession, T> operation)
    {
        lock (gate)
        {
            var working = committed.Copy();
            var result = operation(new MemorySession(working));
            committed = working;
            return result;
        }
    }
}

internal sealed class MemoryState
{
    public Dictionary<Type, Dictionary<Guid, object>> Tables { get; } = [];
    public Dictionary<StockKey, StockBalance> Stock { get; } = [];
    public long Number { get; set; }

    public Dictionary<Guid, object> Table<T>()
    {
        if (!Tables.TryGetValue(typeof(T), out var table)) Tables[typeof(T)] = table = [];
        return table;
    }

    public MemoryState Copy()
    {
        var copy = new MemoryState { Number = Number };
        foreach (var (type, table) in Tables) copy.Tables[type] = new(table);
        foreach (var (key, balance) in Stock) copy.Stock[key] = balance;
        return copy;
    }
}

internal class MemoryRepository<T>(MemoryState state, Func<T, Guid> getId) : IRepository<T>, IAppendRepository<T> where T : class
{
    private Dictionary<Guid, object> Table => state.Table<T>();

    public T? Find(Guid id) => Table.TryGetValue(id, out var item) ? (T)item : null;
    public IReadOnlyList<T> All() => Table.Values.Cast<T>().ToArray();
    public void Add(T entity) => Table.Add(getId(entity), entity);
    public void Update(T entity)
    {
        if (!Table.ContainsKey(getId(entity))) throw new InvalidOperationException("Update requires an existing entity.");
        Table[getId(entity)] = entity;
    }
}

internal sealed class MemoryProducts(MemoryState state) : MemoryRepository<Product>(state, item => item.Id), IProductRepository
{
    public Product? BySku(string sku) => All().SingleOrDefault(x => string.Equals(x.Sku, sku, StringComparison.OrdinalIgnoreCase));
    public Product? ByBarcode(string barcode) => All().SingleOrDefault(x => x.Barcode == barcode);
    public ProductPage Search(string query, bool includeArchived, int skip, int take)
    {
        var matches = All().Where(x => (includeArchived || x.IsActive) &&
            (x.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.Barcode.Contains(query, StringComparison.Ordinal))).OrderBy(x => x.Sku, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
        return new ProductPage(matches.Skip(skip).Take(take).ToArray(), matches.Length);
    }
}

internal sealed class MemoryUsers(MemoryState state) : MemoryRepository<UserAccount>(state, item => item.Id), IUserRepository
{
    public UserAccount? ByLogin(string login) => All().SingleOrDefault(x => string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase));
}

internal sealed class MemoryStock(MemoryState state) : IStockRepository
{
    public StockBalance? Find(StockKey key) => state.Stock.GetValueOrDefault(key);
    public IReadOnlyList<StockBalance> All() => state.Stock.Values.ToArray();
    public void Put(StockBalance balance) => state.Stock[balance.Key] = balance;
}

internal sealed class MemorySession(MemoryState state) : IWarehouseSession
{
    public IProductRepository Products { get; } = new MemoryProducts(state);
    public IRepository<WarehouseSite> Warehouses { get; } = new MemoryRepository<WarehouseSite>(state, item => item.Id);
    public IRepository<MinimumStock> Minimums { get; } = new MemoryRepository<MinimumStock>(state, item => item.Id);
    public IRepository<StockDocument> Documents { get; } = new MemoryRepository<StockDocument>(state, item => item.Id);
    public IRepository<InventorySession> Inventories { get; } = new MemoryRepository<InventorySession>(state, item => item.Id);
    public IUserRepository Users { get; } = new MemoryUsers(state);
    public IStockRepository Stock { get; } = new MemoryStock(state);
    public IAppendRepository<StockMovement> Movements { get; } = new MemoryRepository<StockMovement>(state, item => item.Id);
    public IAppendRepository<AuditEntry> Audit { get; } = new MemoryRepository<AuditEntry>(state, item => item.Id);
    public long NextDocumentNumber() => ++state.Number;
}
