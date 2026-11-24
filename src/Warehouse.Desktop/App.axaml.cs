using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Warehouse.Desktop.Storage;
using Warehouse.Desktop.ViewModels;
using Warehouse.Desktop.Views;

namespace Warehouse.Desktop;

public sealed class App : Avalonia.Application
{
    private readonly DesktopServices services;

    public App() : this(new DesktopServices(new DemoStorage().Connect, DemoStorage.Notice)) { }

    public App(DesktopServices services) => this.services = services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(services) };

        base.OnFrameworkInitializationCompleted();
    }
}
