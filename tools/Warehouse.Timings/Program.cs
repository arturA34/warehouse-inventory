using System.Diagnostics;
using Warehouse.Application;
using Warehouse.Domain;
using Warehouse.Tests.Support;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--client"]) return ClientTimings.Run();
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Использование: Warehouse.Timings [--client]");
            return 2;
        }
        CoreAsync().GetAwaiter().GetResult();
        return 0;
    }

    private static async Task CoreAsync()
    {
        var work = new MemoryWork();
        var actor = new UserAccount(Guid.NewGuid(), "reader", "test", Role.Auditor);
        var user = new TestIdentity { UserId = actor.Id };
        var clock = new TestClock();
        var catalog = new CatalogService(work, user, clock);
        var reports = new ReportService(work, user, clock, new TestExporter());
        TimingFixture.Seed(work, [actor]);

        void Search() => TimingFixture.Search(catalog);

        void Quarter()
        {
            var rows = reports.Turnover(TimingFixture.From, TimingFixture.To);
            if (rows.Count != TimingFixture.Products || rows.Sum(x => x.ClosingQuantity) != 2 * TimingFixture.Products)
                throw new InvalidOperationException("Неверный итог отчёта.");
        }

        Search();
        Quarter();
        TimingFixture.Print("Search", Enumerable.Range(0, 11).Select(_ => Measure(Search)));
        TimingFixture.Print("Quarter", Enumerable.Range(0, 7).Select(_ => Measure(Quarter)));

        using var ready = new Barrier(TimingFixture.Clients);
        var clients = Enumerable.Range(0, TimingFixture.Clients).Select(_ => Task.Factory.StartNew(() =>
        {
            ready.SignalAndWait();
            return Measure(Search);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        TimingFixture.Print("Concurrent search", await Task.WhenAll(clients));
    }

    private static double Measure(Action action)
    {
        var timer = Stopwatch.StartNew();
        action();
        return timer.Elapsed.TotalMilliseconds;
    }
}
