using Warehouse.Application;
using Warehouse.Domain;

internal static class TimingFixture
{
    public const int Products = 100_000;
    public const int Clients = 20;
    public const string Query = "SKU-09";
    public static readonly DateTimeOffset From = new(new DateTime(2026, 1, 1));
    public static readonly DateTimeOffset To = new DateTimeOffset(new DateTime(2026, 4, 1)).AddTicks(-1);

    public static void Seed(IUnitOfWork work, IEnumerable<UserAccount> users) => work.Execute(session =>
    {
        foreach (var user in users) session.Users.Add(user);
        var sites = Enumerable.Range(1, 10).Select(i => new WarehouseSite(Guid.NewGuid(), $"Склад {i}")).ToArray();
        foreach (var site in sites) session.Warehouses.Add(site);
        for (var i = 0; i < Products; i++)
        {
            var product = new Product(Guid.NewGuid(), $"SKU-{i:D6}", $"{i:D12}", $"Товар {i}", "шт");
            session.Products.Add(product);
            var key = new StockKey(sites[i % sites.Length].Id, product.Id);
            session.Stock.Put(new StockBalance(key, 2, 20));
            void Movement(int day, int quantity) => session.Movements.Add(new StockMovement(
                Guid.NewGuid(), Guid.NewGuid(), key, quantity, quantity * 10, From.AddDays(day)));
            Movement(i % 90, 2);
            if (i % 10 == 0)
            {
                Movement(-1, 1);
                Movement(89, -1);
            }
        }
        return true;
    });

    public static void Search(CatalogService catalog)
    {
        var page = catalog.Search(Query);
        if (page.Total != 10_000 || page.Items.Count != 50) throw new InvalidOperationException("Неполная выдача.");
    }

    public static void Print(string name, IEnumerable<double> measurements)
    {
        var sorted = measurements.Order().ToArray();
        var median = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
        Console.WriteLine($"{name} (n={sorted.Length}): median={median:F1} ms, max={sorted[^1]:F1} ms");
    }
}
