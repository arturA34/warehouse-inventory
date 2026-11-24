using Warehouse.Application;
using Warehouse.Desktop.Storage;

namespace Warehouse.Desktop.ViewModels;

public sealed class MainViewModel : ViewModel
{
    private ViewModel screen;
    private readonly DesktopServices services;

    public MainViewModel(DesktopServices services)
    {
        this.services = services;
        SignIn = new SignInViewModel(services, ShowWorkspace);
        screen = SignIn;
    }

    public SignInViewModel SignIn { get; }

    public ViewModel Screen
    {
        get => screen;
        private set => Set(ref screen, value);
    }

    private void ShowWorkspace(DesktopSession session, SignInResult account, StorageChoice mode) =>
        Screen = new WorkspaceViewModel(account, mode, services.StorageNotice, session, () =>
        {
            session.SignOut();
            Screen = SignIn;
        });
}
