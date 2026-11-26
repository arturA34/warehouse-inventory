using Avalonia.Controls;
using Avalonia.Input;
using Warehouse.Desktop.ViewModels;

namespace Warehouse.Desktop.Views;

public sealed partial class InventoryView : UserControl
{
    public InventoryView() => InitializeComponent();

    private void OnScannerKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || DataContext is not InventoryViewModel model) return;
        args.Handled = true;
        model.Scan.Execute(null);
        Scanner.SelectAll();
    }
}
