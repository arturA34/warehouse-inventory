using Warehouse.Desktop.Storage;
using Warehouse.Desktop.ViewModels;
using Warehouse.Domain;

namespace Warehouse.Tests;

public sealed class DesktopSignInTests
{
    [Theory]
    [InlineData("admin", Role.Administrator, true)]
    [InlineData("keeper", Role.Storekeeper, true)]
    [InlineData("auditor", Role.Auditor, false)]
    public async Task SignInOpensSelectedModeWithTheAccountRole(string login, Role role, bool canManageCatalog)
    {
        var demo = new DemoStorage();
        StorageMode? openedMode = null;
        var main = new MainViewModel(new DesktopServices(mode =>
        {
            openedMode = mode;
            return demo.Connect(mode);
        }));
        main.SignIn.SelectedMode = main.SignIn.Modes[1];
        main.SignIn.Login = login;

        await main.SignIn.SignInAsync("demo");

        var workspace = Assert.IsType<WorkspaceViewModel>(main.Screen);
        Assert.Equal(StorageMode.PostgreSql, openedMode);
        Assert.Equal(role, workspace.Account.Role);
        Assert.Equal(canManageCatalog, workspace.Allows(Permission.ManageCatalog));
        workspace.SignOut.Execute(null);
        Assert.Same(main.SignIn, main.Screen);
    }

    [Fact]
    public async Task InvalidPasswordsLockOnlyTheAccountInTheSelectedMode()
    {
        var demo = new DemoStorage();
        var main = new MainViewModel(new DesktopServices(demo.Connect));
        var signIn = main.SignIn;
        signIn.Login = "keeper";

        await signIn.SignInAsync("wrong");
        Assert.Same(signIn, main.Screen);
        Assert.Equal("Неверный логин или пароль.", signIn.Error);

        for (var attempt = 1; attempt < UserAccount.FailureLimit; attempt++)
            await signIn.SignInAsync("wrong");

        await signIn.SignInAsync("demo");
        Assert.Same(signIn, main.Screen);
        Assert.Equal("Учётная запись заблокирована.", signIn.Error);

        signIn.SelectedMode = signIn.Modes[1];
        await signIn.SignInAsync("demo");
        Assert.IsType<WorkspaceViewModel>(main.Screen);
    }
}
