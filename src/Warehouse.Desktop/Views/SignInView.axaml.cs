using Avalonia.Controls;
using Avalonia.Interactivity;
using Warehouse.Desktop.ViewModels;

namespace Warehouse.Desktop.Views;

public sealed partial class SignInView : UserControl
{
    public SignInView() => InitializeComponent();

    private async void OnSignIn(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SignInViewModel model) return;
        var password = PasswordBox.Text ?? "";
        PasswordBox.Clear();
        await model.SignInAsync(password);
        if (DataContext == model) PasswordBox.Focus();
    }
}
