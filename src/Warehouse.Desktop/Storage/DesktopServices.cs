using Warehouse.Application;

namespace Warehouse.Desktop.Storage;

public enum StorageMode { Sqlite, PostgreSql }

public sealed record StorageConnection(IUnitOfWork Work, IPasswordHasher Passwords, IReportExporter Exporter);

public sealed class DesktopServices(Func<StorageMode, StorageConnection> connect, string? storageNotice = null)
{
    public string? StorageNotice { get; } = storageNotice;

    internal DesktopSession Open(StorageMode mode) => new(connect(mode));
}

internal sealed class DesktopSession(StorageConnection storage)
{
    private readonly CurrentUser user = new();
    private readonly SystemClock clock = new();

    public CatalogService Catalog => new(storage.Work, user, clock);
    public DocumentService Documents => new(storage.Work, user, clock);
    public InventoryService Inventories => new(storage.Work, user, clock);
    public ReportService Reports => new(storage.Work, user, clock, storage.Exporter);
    public PrintFormService PrintForms => new(storage.Work, user, clock);
    public IReportExporter Exporter => storage.Exporter;

    public SignInResult SignIn(string login, string password)
    {
        var result = new AccountService(storage.Work, user, clock, storage.Passwords).SignIn(login, password);
        user.UserId = result.UserId;
        return result;
    }

    public void SignOut() => user.UserId = null;

    private sealed class CurrentUser : ICurrentUser { public Guid? UserId { get; set; } }
    private sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
