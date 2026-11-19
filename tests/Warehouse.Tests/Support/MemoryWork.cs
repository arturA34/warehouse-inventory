using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Tests.Support;

// Для тестов храним данные в памяти. Изменения становятся видны после фиксации транзакции.
internal sealed class MemoryWork : IUnitOfWork
{
    private readonly object gate = new();
    private MemoryState committed = new();
    public int ConflictsRemaining { get; set; }
    public bool FailCommit { get; set; }
    public int Attempts { get; private set; }
    public int Commits { get; private set; }

    public T Execute<T>(Func<IWarehouseSession, T> operation)
    {
        lock (gate)
        {
            Attempts++;
            var working = committed.Copy();
            var result = operation(new MemorySession(working));
            if (ConflictsRemaining > 0)
            {
                ConflictsRemaining--;
                throw new ConcurrencyConflictException();
            }
            if (FailCommit) throw new IOException("Simulated storage failure before commit.");
            committed = working;
            Commits++;
            return result;
        }
    }

    public void Seed<T>(T entity) where T : class => Execute(s =>
    {
        var repository = new MemoryRepository<T>(((MemorySession)s).State);
        repository.Add(entity);
        return true;
    });
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

internal class MemoryRepository<T>(MemoryState state) : IRepository<T>, IAppendRepository<T> where T : class
{
    private Dictionary<Guid, object> Table => state.Table<T>();
    private static Guid Id(T entity) => (Guid)typeof(T).GetProperty("Id")!.GetValue(entity)!;
    public T? Find(Guid id) => Table.TryGetValue(id, out var item) ? (T)item : null;
    public IReadOnlyList<T> All() => Table.Values.Cast<T>().ToArray();
    public void Add(T entity) => Table.Add(Id(entity), entity);
    public void Update(T entity)
    {
        if (!Table.ContainsKey(Id(entity))) throw new InvalidOperationException("Update requires an existing entity.");
        Table[Id(entity)] = entity;
    }
}

internal sealed class MemoryProducts(MemoryState state) : MemoryRepository<Product>(state), IProductRepository
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

internal sealed class MemoryUsers(MemoryState state) : MemoryRepository<UserAccount>(state), IUserRepository
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
    public MemoryState State => state;
    public IProductRepository Products { get; } = new MemoryProducts(state);
    public IRepository<WarehouseSite> Warehouses { get; } = new MemoryRepository<WarehouseSite>(state);
    public IRepository<MinimumStock> Minimums { get; } = new MemoryRepository<MinimumStock>(state);
    public IRepository<StockDocument> Documents { get; } = new MemoryRepository<StockDocument>(state);
    public IRepository<InventorySession> Inventories { get; } = new MemoryRepository<InventorySession>(state);
    public IUserRepository Users { get; } = new MemoryUsers(state);
    public IStockRepository Stock { get; } = new MemoryStock(state);
    public IAppendRepository<StockMovement> Movements { get; } = new MemoryRepository<StockMovement>(state);
    public IAppendRepository<AuditEntry> Audit { get; } = new MemoryRepository<AuditEntry>(state);
    public long NextDocumentNumber() => ++state.Number;
}

internal sealed class TestIdentity : ICurrentUser { public Guid? UserId { get; set; } }
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
}

// Тестовый хешер нужен только для проверки сценариев входа.
internal sealed class TestPasswords : IPasswordHasher
{
    public string Hash(string password) => "test-only:" + password;
    public bool Verify(string password, string hash) => hash == Hash(password);
}

internal sealed class TestExporter : IReportExporter
{
    public ReportTable? LastReport { get; private set; }
    public ExportFormat? LastFormat { get; private set; }
    public int Calls { get; private set; }
    public ReportFile Export(ReportTable report, ExportFormat format)
    {
        Calls++;
        LastReport = report;
        LastFormat = format;
        return new ReportFile("test", "application/test", new byte[] { 1, 2, 3 });
    }
}

internal sealed class World
{
    public MemoryWork Work { get; } = new();
    public TestIdentity Identity { get; } = new();
    public TestClock Clock { get; } = new();
    public TestPasswords Passwords { get; } = new();
    public TestExporter Exporter { get; } = new();
    public Guid Admin { get; } = Guid.NewGuid();
    public Guid Keeper { get; } = Guid.NewGuid();
    public Guid Auditor { get; } = Guid.NewGuid();
    public CatalogService Catalog { get; }
    public DocumentService Documents { get; }
    public InventoryService Inventories { get; }
    public AccountService Accounts { get; }
    public ReportService Reports { get; }
    public WarehouseSite A { get; }
    public WarehouseSite B { get; }
    public Product P { get; }
    public Product Q { get; }

    public World()
    {
        Work.Seed(new UserAccount(Admin, "admin", Passwords.Hash("password"), Role.Administrator));
        Work.Seed(new UserAccount(Keeper, "keeper", Passwords.Hash("password"), Role.Storekeeper));
        Work.Seed(new UserAccount(Auditor, "auditor", Passwords.Hash("password"), Role.Auditor));
        Identity.UserId = Admin;
        Catalog = new(Work, Identity, Clock);
        Documents = new(Work, Identity, Clock);
        Inventories = new(Work, Identity, Clock);
        Accounts = new(Work, Identity, Clock, Passwords);
        Reports = new(Work, Identity, Clock, Exporter);
        A = Catalog.CreateWarehouse("A");
        B = Catalog.CreateWarehouse("B");
        P = Catalog.CreateProduct("P", "00123", "Product P", "pcs");
        Q = Catalog.CreateProduct("Q", "00456", "Product Q", "pcs");
    }

    public StockDocument Receipt(decimal quantity, decimal price, Guid? warehouseId = null, Guid? productId = null) =>
        Documents.Post(Documents.Create(DocumentKind.Receipt, warehouseId ?? A.Id, [new(productId ?? P.Id, quantity, price)]).Id);

    public StockDocument Issue(decimal quantity, Guid? warehouseId = null, Guid? productId = null) =>
        Documents.Post(Documents.Create(DocumentKind.Issue, warehouseId ?? A.Id, [new(productId ?? P.Id, quantity)]).Id);

    public StockBalance Stock(Guid? warehouseId = null, Guid? productId = null) => Work.Execute(s =>
    {
        var key = new StockKey(warehouseId ?? A.Id, productId ?? P.Id);
        return s.Stock.Find(key) ?? new StockBalance(key);
    });

    public IReadOnlyList<T> Read<T>(Func<IWarehouseSession, IReadOnlyList<T>> read) => Work.Execute(read);
}
