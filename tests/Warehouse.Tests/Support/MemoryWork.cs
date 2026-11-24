using Warehouse.Application;
using Warehouse.Desktop.Storage;
using Warehouse.Domain;

namespace Warehouse.Tests.Support;

internal sealed class MemoryWork : IUnitOfWork
{
    private readonly MemoryUnitOfWork work = new();
    public int ConflictsRemaining { get; set; }
    public bool FailCommit { get; set; }
    public int Attempts { get; private set; }
    public int Commits { get; private set; }

    public T Execute<T>(Func<IWarehouseSession, T> operation) => work.Execute(session =>
    {
        Attempts++;
        var result = operation(session);
        if (ConflictsRemaining > 0)
        {
            ConflictsRemaining--;
            throw new ConcurrencyConflictException();
        }
        if (FailCommit) throw new IOException("Simulated storage failure before commit.");
        Commits++;
        return result;
    });

    public void Seed(UserAccount entity) => Execute(s => { s.Users.Add(entity); return true; });
    public void Seed(StockDocument entity) => Execute(s => { s.Documents.Add(entity); return true; });
    public void Seed(StockMovement entity) => Execute(s => { s.Movements.Add(entity); return true; });
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
