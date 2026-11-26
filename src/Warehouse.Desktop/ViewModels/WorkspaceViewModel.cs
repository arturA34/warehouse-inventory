using System.Windows.Input;
using Warehouse.Application;
using Warehouse.Desktop.Storage;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public sealed class WorkspaceViewModel : ViewModel
{
    private int selectedTab;
    internal WorkspaceViewModel(SignInResult account, StorageChoice mode, string? storageNotice,
        DesktopSession session, Action signOut)
    {
        Account = account;
        StorageMode = mode.Name;
        StorageNotice = storageNotice;
        SignOut = new Command(signOut);
        Reports = new ReportsViewModel(session.Reports, session.Catalog, session.Inventories, session.Exporter,
            session.PrintForms, session.Documents);
        Catalog = new CatalogViewModel(session.Catalog, session.Reports, Allows(Permission.ManageCatalog));
        Documents = new DocumentsViewModel(session.Documents, session.Catalog, session.Reports,
            Allows(Permission.ManageDocuments), Allows(Permission.ReverseDocuments), () => Catalog.RefreshBalances.Execute(null));
        Inventories = new InventoryViewModel(session.Inventories, session.Catalog, session.Documents, session.Reports,
            Allows(Permission.Inventory), () =>
            {
                Catalog.RefreshBalances.Execute(null);
                Documents.Refresh.Execute(null);
            }, id =>
            {
                Documents.Show(id);
                SelectedTab = 1;
            });
    }

    public SignInResult Account { get; }
    public string Identity => $"{Account.Login} · {RoleName(Account.Role)}";
    public string StorageMode { get; }
    public string? StorageNotice { get; }
    public ICommand SignOut { get; }
    public CatalogViewModel Catalog { get; }
    public DocumentsViewModel Documents { get; }
    public InventoryViewModel Inventories { get; }
    public ReportsViewModel Reports { get; }
    public int SelectedTab
    {
        get => selectedTab;
        set
        {
            if (Set(ref selectedTab, value) && value == 3) Reports.RefreshSources();
        }
    }

    public bool Allows(Permission permission) => AccessPolicy.Allows(Account.Role, permission);

    private static string RoleName(Role role) => role switch
    {
        Role.Administrator => "Администратор",
        Role.Storekeeper => "Кладовщик",
        Role.Auditor => "Ревизор",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
