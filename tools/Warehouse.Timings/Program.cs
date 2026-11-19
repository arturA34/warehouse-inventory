using System.Diagnostics;
using Warehouse.Application;
using Warehouse.Domain;
using Warehouse.Tests.Support;

const int productCount = 100_000;
const int clientCount = 20;
var work = new MemoryWork();
var user = new TestIdentity { UserId = Guid.NewGuid() };
var clock = new TestClock();
var catalog = new CatalogService(work, user, clock);
var reports = new ReportService(work, user, clock, new TestExporter());
var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var end = start.AddMonths(3).AddTicks(-1);

work.Execute(session =>
{
    session.Users.Add(new UserAccount(user.UserId.Value, "reader", "test", Role.Auditor));
    var sites = Enumerable.Range(1, 10).Select(i => new WarehouseSite(Guid.NewGuid(), $"Склад {i}")).ToArray();
    foreach (var site in sites) session.Warehouses.Add(site);
    for (var i = 0; i < productCount; i++)
    {
        var product = new Product(Guid.NewGuid(), $"SKU-{i:D6}", $"{i:D12}", $"Товар {i}", "шт");
        session.Products.Add(product);
        var key = new StockKey(sites[i % sites.Length].Id, product.Id);
        void Movement(int day, int quantity) => session.Movements.Add(new StockMovement(
            Guid.NewGuid(), Guid.NewGuid(), key, quantity, quantity * 10, start.AddDays(day)));
        Movement(i % 90, 2);
        if (i % 10 == 0)
        {
            Movement(-1, 1);
            Movement(89, -1);
        }
    }
    return true;
});

void Search()
{
    var page = catalog.Search("SKU-09");
    if (page.Total != 10_000 || page.Items.Count != 50) throw new InvalidOperationException("Неполная выдача.");
}

void Quarter()
{
    var rows = reports.Turnover(start, end);
    if (rows.Count != productCount || rows.Sum(x => x.ClosingQuantity) != 2 * productCount)
        throw new InvalidOperationException("Неверный итог отчёта.");
}

double Measure(Action action)
{
    var timer = Stopwatch.StartNew();
    action();
    return timer.Elapsed.TotalMilliseconds;
}

void Print(string name, IEnumerable<double> measurements)
{
    var sorted = measurements.Order().ToArray();
    var median = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
    Console.WriteLine($"{name} (n={sorted.Length}): median={median:F1} ms, max={sorted[^1]:F1} ms");
}

Search();
Quarter();
Print("Search", Enumerable.Range(0, 11).Select(_ => Measure(Search)));
Print("Quarter", Enumerable.Range(0, 7).Select(_ => Measure(Quarter)));

// Все клиенты начинают поиск вместе. Время ожидания общей блокировки входит в замер.
using var ready = new Barrier(clientCount);
var clients = Enumerable.Range(0, clientCount).Select(_ => Task.Factory.StartNew(() =>
{
    ready.SignalAndWait();
    return Measure(Search);
}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
Print("Concurrent search", await Task.WhenAll(clients));
