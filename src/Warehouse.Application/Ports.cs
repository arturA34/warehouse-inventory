using Warehouse.Domain;

namespace Warehouse.Application;

public interface IReadRepository<T>
{
    T? Find(Guid id);
    IReadOnlyList<T> All();
}

public interface IRepository<T> : IReadRepository<T>
{
    void Add(T entity);
    void Update(T entity);
}

/// <summary>Append-only port: historical movements and audit entries cannot be edited or removed.</summary>
public interface IAppendRepository<T> : IReadRepository<T>
{
    void Add(T entity);
}

public sealed record ProductPage(IReadOnlyList<Product> Items, int Total);

public interface IProductRepository : IRepository<Product>
{
    // SKU and login uniqueness is ordinal case-insensitive; barcodes are trimmed, ordinal exact strings.
    Product? BySku(string sku);
    Product? ByBarcode(string barcode);
    ProductPage Search(string query, bool includeArchived, int skip, int take);
}

public interface IUserRepository : IRepository<UserAccount>
{
    UserAccount? ByLogin(string login);
}

public interface IStockRepository
{
    StockBalance? Find(StockKey key);
    IReadOnlyList<StockBalance> All();
    void Put(StockBalance balance);
}

public interface IWarehouseSession
{
    IProductRepository Products { get; }
    IRepository<WarehouseSite> Warehouses { get; }
    IRepository<MinimumStock> Minimums { get; }
    IRepository<StockDocument> Documents { get; }
    IRepository<InventorySession> Inventories { get; }
    IUserRepository Users { get; }
    IStockRepository Stock { get; }
    IAppendRepository<StockMovement> Movements { get; }
    IAppendRepository<AuditEntry> Audit { get; }
    long NextDocumentNumber();
}

/// <summary>
/// Executes a fresh, serializable transaction and commits only if the callback succeeds.
/// Roll back ALL writes on error, including audit and numbering. Dispose before returning.
/// Enforce unique SKU/barcode/login, unique stock keys and active inventory locks in storage.
/// Concurrent stock writes and inventory starts must conflict, including absent stock rows.
/// Translate serialization/version/uniqueness races into ConcurrencyConflictException.
/// Callbacks must not perform external side effects: a conflict reruns the entire callback.
/// </summary>
public interface IUnitOfWork
{
    T Execute<T>(Func<IWarehouseSession, T> operation);
}

public sealed class ConcurrencyConflictException(string message = "Concurrent change detected.") : Exception(message);

/// <summary>Trusted host identity established by authentication, never a role supplied by the caller.</summary>
public interface ICurrentUser { Guid? UserId { get; } }
public interface IClock { DateTimeOffset UtcNow { get; } }

/// <summary>Production infrastructure must implement Argon2id with per-password salts.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public enum ExportFormat { Excel, Pdf }
public sealed record ReportTable(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
public sealed record ReportFile(string Name, string ContentType, ReadOnlyMemory<byte> Content);

/// <summary>Implemented with ClosedXML and QuestPDF in infrastructure.</summary>
public interface IReportExporter { ReportFile Export(ReportTable report, ExportFormat format); }
