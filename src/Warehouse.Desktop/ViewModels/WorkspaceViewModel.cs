using System.Windows.Input;
using Warehouse.Application;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public sealed class WorkspaceViewModel : ViewModel
{
    internal WorkspaceViewModel(SignInResult account, StorageChoice mode, string? storageNotice, Action signOut)
    {
        Account = account;
        StorageMode = mode.Name;
        StorageNotice = storageNotice;
        SignOut = new Command(signOut);
    }

    public SignInResult Account { get; }
    public string Identity => $"{Account.Login} · {RoleName(Account.Role)}";
    public string StorageMode { get; }
    public string? StorageNotice { get; }
    public ICommand SignOut { get; }

    public bool Allows(Permission permission) => AccessPolicy.Allows(Account.Role, permission);

    private static string RoleName(Role role) => role switch
    {
        Role.Administrator => "Администратор",
        Role.Storekeeper => "Кладовщик",
        Role.Auditor => "Ревизор",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
