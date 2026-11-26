using System.Security.Cryptography;
using QuestPDF.Infrastructure;
using Warehouse.Application;
using Warehouse.Desktop.Reports;
using Warehouse.Domain;

namespace Warehouse.Desktop.Storage;

internal sealed class DemoStorage
{
    internal const string Notice = "Демонстрационный режим. Данные хранятся в памяти до закрытия клиента.";
    private readonly Lazy<StorageConnection> local = new(Create);
    private readonly Lazy<StorageConnection> network = new(Create);

    public StorageConnection Connect(StorageMode mode) => mode switch
    {
        StorageMode.Sqlite => local.Value,
        StorageMode.PostgreSql => network.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static StorageConnection Create()
    {
        var work = new MemoryUnitOfWork();
        var passwords = new DemoPasswords();
        work.Execute(session =>
        {
            session.Users.Add(new UserAccount(Guid.NewGuid(), "admin", passwords.Hash("demo"), Role.Administrator));
            session.Users.Add(new UserAccount(Guid.NewGuid(), "keeper", passwords.Hash("demo"), Role.Storekeeper));
            session.Users.Add(new UserAccount(Guid.NewGuid(), "auditor", passwords.Hash("demo"), Role.Auditor));
            SeedCatalog(session);
            return true;
        });
        return new StorageConnection(work, passwords,
            new DesktopReportExporter(new ExcelReportExporter(), new PdfReportExporter(LicenseType.Community)));
    }

    private static void SeedCatalog(IWarehouseSession session)
    {
        var main = new WarehouseSite(Guid.NewGuid(), "Основной склад");
        var reserve = new WarehouseSite(Guid.NewGuid(), "Резервный склад");
        session.Warehouses.Add(main);
        session.Warehouses.Add(reserve);

        var tape = new Product(Guid.NewGuid(), "UP-001", "00123", "Лента упаковочная", "шт.");
        var boxes = new Product(Guid.NewGuid(), "UP-002", "00456", "Коробка картонная", "шт.");
        var archive = new Product(Guid.NewGuid(), "UP-003", "00789", "Пакет бумажный", "шт.", isActive: false);
        foreach (var product in new[] { tape, boxes, archive })
            session.Products.Add(product);

        var tapeKey = new StockKey(main.Id, tape.Id);
        session.Stock.Put(new StockBalance(tapeKey, 12, 360));
        session.Minimums.Add(new MinimumStock(Guid.NewGuid(), tapeKey, 12));
        session.Minimums.Add(new MinimumStock(Guid.NewGuid(), new StockKey(reserve.Id, boxes.Id), 5));
    }

    private sealed class DemoPasswords : IPasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            return Convert.ToBase64String([..salt, ..Derive(password, salt)]);
        }

        public bool Verify(string password, string hash)
        {
            Span<byte> data = stackalloc byte[SaltSize + HashSize];
            return Convert.TryFromBase64String(hash, data, out var size) && size == data.Length &&
                CryptographicOperations.FixedTimeEquals(data[SaltSize..], Derive(password, data[..SaltSize]));
        }

        private static byte[] Derive(string password, ReadOnlySpan<byte> salt) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, HashSize);
    }

}
