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

/// <summary>Движения и записи аудита можно только читать и добавлять.</summary>
public interface IAppendRepository<T> : IReadRepository<T>
{
    void Add(T entity);
}

public sealed record ProductPage(IReadOnlyList<Product> Items, int Total);

public interface IProductRepository : IRepository<Product>
{
    // Артикул сравниваем без учёта регистра (OrdinalIgnoreCase), штрихкод — посимвольно после Trim.
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
/// Каждый вызов открывает новую сериализуемую транзакцию и закрывает её до возврата.
/// При ошибке откатываем всё, включая номер документа и запись аудита.
/// База обеспечивает уникальность артикулов, штрихкодов, логинов, названий складов и ключей остатков и порогов. Логины сравниваются через OrdinalIgnoreCase.
/// Параллельные записи одного остатка конфликтуют. Активная ревизия блокирует и движение, и вторую ревизию той же позиции, даже при нулевом остатке.
/// Конфликты версий, сериализации и гонки при проверке уникальности переводим в ConcurrencyConflictException.
/// При конфликте операция выполняется заново, поэтому файлы записываем после транзакции.
/// </summary>
public interface IUnitOfWork
{
    T Execute<T>(Func<IWarehouseSession, T> operation);
}

public sealed class ConcurrencyConflictException(string message = "Concurrent change detected.") : Exception(message);

/// <summary>Хост передаёт идентификатор вошедшего пользователя. Его текущую роль сервис читает из хранилища.</summary>
public interface ICurrentUser { Guid? UserId { get; } }
public interface IClock { DateTimeOffset UtcNow { get; } }

/// <summary>В рабочем приложении используется Argon2id с отдельной солью для каждого пароля.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public enum ExportFormat { Excel, Pdf }
public sealed record ReportTable(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public IReadOnlyList<int> NumericColumns { get; init; } = [];
    public IReadOnlyList<string> Details { get; init; } = [];
    public IReadOnlyList<string>? Totals { get; init; }
    public bool DraftPrintForm { get; init; }
}
public sealed record ReportFile(string Name, string ContentType, ReadOnlyMemory<byte> Content);

/// <summary>За запись файлов отвечают ClosedXML и QuestPDF в инфраструктурном слое.</summary>
public interface IReportExporter { ReportFile Export(ReportTable report, ExportFormat format); }
