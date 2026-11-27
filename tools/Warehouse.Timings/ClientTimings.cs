using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuestPDF.Infrastructure;
using Warehouse.Application;
using Warehouse.Desktop;
using Warehouse.Desktop.Reports;
using Warehouse.Desktop.Storage;
using Warehouse.Desktop.ViewModels;
using Warehouse.Desktop.Views;
using Warehouse.Domain;
using Warehouse.Tests.Support;

internal static class ClientTimings
{
    public static int Run()
    {
        var passwords = new TestPasswords();
        var accounts = Enumerable.Range(1, TimingFixture.Clients)
            .Select(index => new UserAccount(Guid.NewGuid(), $"reader{index}", passwords.Hash("timing"), Role.Auditor)).ToArray();
        var work = new MemoryUnitOfWork();
        TimingFixture.Seed(work, accounts);
        var exporter = new DesktopReportExporter(new ExcelReportExporter(), new PdfReportExporter(LicenseType.Community));
        var connection = new StorageConnection(work, passwords, exporter);
        var services = new DesktopServices(_ => connection, "Измерение клиента · синтетическое хранилище в памяти.");
        var background = accounts.Skip(1)
            .Select(user => new CatalogService(work, new TestIdentity { UserId = user.Id }, new TestClock())).ToArray();

        Console.WriteLine($"{RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}; logical CPUs={Environment.ProcessorCount}; " +
            $"available memory={GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MiB; .NET {Environment.Version}");
        Console.WriteLine("In-memory baseline; no DB or network; 100000 products, 10 warehouses, 120000 movements, 20 clients.");
        return AppBuilder.Configure(() => new App(services)).UsePlatformDetect().AfterSetup(builder =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var lifetime = (IClassicDesktopStyleApplicationLifetime)builder.Instance!.ApplicationLifetime!;
                try
                {
                    var window = (MainWindow)lifetime.MainWindow!;
                    await Painted(window);
                    var main = (MainViewModel)window.DataContext!;
                    main.SignIn.Login = accounts[0].Login;
                    await main.SignIn.SignInAsync("timing");
                    var workspace = (WorkspaceViewModel)main.Screen;
                    await Painted(window);
                    await MeasureScenarios(window, workspace, background);
                    lifetime.Shutdown();
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(error);
                    lifetime.Shutdown(1);
                }
            });
        }).StartWithClassicDesktopLifetime([]);
    }

    private static async Task MeasureScenarios(Window window, WorkspaceViewModel workspace, CatalogService[] background)
    {
        double searchBeforeFrame = 0;
        async Task Navigate()
        {
            workspace.SelectedTab = 1;
            await Painted(window);
            if (!window.GetVisualDescendants().OfType<DocumentsView>().Any())
                throw new InvalidOperationException("Вкладка документов не появилась.");
        }

        async Task Search()
        {
            var timer = Stopwatch.StartNew();
            workspace.Catalog.Query = TimingFixture.Query;
            workspace.Catalog.Search.Execute(null);
            searchBeforeFrame = timer.Elapsed.TotalMilliseconds;
            await Painted(window);
            if (workspace.Catalog.Products.Count != 25 || !workspace.Catalog.HasNextPage)
                throw new InvalidOperationException("Результаты поиска неполные.");
        }

        async Task Quarter()
        {
            await workspace.Reports.GenerateAsync();
            await Painted(window);
            var report = workspace.Reports.Table;
            if (report?.Rows.Count != TimingFixture.Products || report.Rows[0][8] != "2" ||
                !window.GetVisualDescendants().OfType<DataGrid>().Any(x => x.IsVisible))
                throw new InvalidOperationException("Квартальный отчёт не отобразился.");
        }

        await Navigate();
        workspace.SelectedTab = 0;
        await Painted(window);
        await Search();
        workspace.SelectedTab = 3;
        workspace.Reports.SelectedReport = workspace.Reports.Choices.Single(x => x.Kind == ReportKind.Turnover);
        workspace.Reports.From = TimingFixture.From;
        workspace.Reports.Until = TimingFixture.To;
        await Quarter();

        var navigation = new List<double>();
        var searches = new List<double>();
        var searchesBeforeFrame = new List<double>();
        var quarters = new List<double>();
        for (var round = 0; round < 5; round++)
        {
            workspace.SelectedTab = 0;
            await Painted(window);
            navigation.Add(await MeasureUnderLoad(Navigate, background));
            workspace.SelectedTab = 0;
            await Painted(window);
            searches.Add(await MeasureUnderLoad(Search, background));
            searchesBeforeFrame.Add(searchBeforeFrame);
            workspace.SelectedTab = 3;
            await Painted(window);
            quarters.Add(await MeasureUnderLoad(Quarter, background));
        }
        TimingFixture.Print("Tab until render", navigation);
        TimingFixture.Print("Search until render", searches);
        var slowestSearch = searches.IndexOf(searches.Max());
        Console.WriteLine($"Slowest search before render request: {searchesBeforeFrame[slowestSearch]:F1} ms");
        TimingFixture.Print("Quarter until render", quarters);

        var saving = new List<double>();
        for (var round = 0; round < 3; round++)
        {
            var path = Path.Combine(Path.GetTempPath(), $"warehouse-timing-{Guid.NewGuid():N}.xlsx");
            try
            {
                var timer = Stopwatch.StartNew();
                await workspace.Reports.SaveAsync(ExportFormat.Excel, async file =>
                {
                    await File.WriteAllBytesAsync(path, file.Content);
                    return true;
                });
                if (!File.Exists(path)) throw new IOException(workspace.Reports.Error);
                saving.Add(timer.Elapsed.TotalMilliseconds);
            }
            finally
            {
                File.Delete(path);
            }
        }
        TimingFixture.Print("Excel generation and disk write", saving);
    }

    private static async Task<double> MeasureUnderLoad(Func<Task> action, CatalogService[] background)
    {
        using var ready = new Barrier(background.Length + 1);
        var clients = background.Select(catalog => Task.Factory.StartNew(() =>
        {
            if (!ready.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Не вышли на старт замера.");
            TimingFixture.Search(catalog);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        if (!ready.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Не вышли на старт замера.");
        var timer = Stopwatch.StartNew();
        try
        {
            await action();
            return timer.Elapsed.TotalMilliseconds;
        }
        finally
        {
            await Task.WhenAll(clients);
        }
    }

    private static Task Painted(TopLevel window)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // На втором кадре Avalonia уже обработала первый кадр с обновлёнными данными.
        window.RequestAnimationFrame(_ => window.RequestAnimationFrame(_ => ready.SetResult()));
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
