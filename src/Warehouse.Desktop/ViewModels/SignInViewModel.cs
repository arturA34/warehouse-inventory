using System.Data.Common;
using Warehouse.Application;
using Warehouse.Desktop.Storage;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public sealed record StorageChoice(StorageMode Mode, string Name)
{
    public override string ToString() => Name;
}

public sealed class SignInViewModel : ViewModel
{
    private readonly DesktopServices services;
    private readonly Action<DesktopSession, SignInResult, StorageChoice> signedIn;
    private StorageChoice? selectedMode;
    private string login = "";
    private string error = "";
    private bool isBusy;

    internal SignInViewModel(DesktopServices services, Action<DesktopSession, SignInResult, StorageChoice> signedIn)
    {
        this.services = services;
        this.signedIn = signedIn;
        selectedMode = Modes[0];
    }

    public IReadOnlyList<StorageChoice> Modes { get; } =
    [
        new(StorageMode.Sqlite, "Автономный · SQLite"),
        new(StorageMode.PostgreSql, "Сетевой · PostgreSQL")
    ];

    public StorageChoice? SelectedMode { get => selectedMode; set => Set(ref selectedMode, value); }
    public string Login { get => login; set => Set(ref login, value ?? ""); }
    public string Error { get => error; private set => Set(ref error, value); }
    public string? StorageNotice => services.StorageNotice;
    public bool IsDemo => StorageNotice is not null;
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }

    public async Task SignInAsync(string password)
    {
        if (IsBusy) return;
        Error = "";
        if (SelectedMode is not { } mode || string.IsNullOrWhiteSpace(Login) || password.Length == 0)
        {
            Error = "Выберите режим и введите логин и пароль.";
            return;
        }

        IsBusy = true;
        var enteredLogin = Login;
        try
        {
            var (session, account) = await Task.Run(() =>
            {
                var session = services.Open(mode.Mode);
                return (session, session.SignIn(enteredLogin, password));
            });
            signedIn(session, account, mode);
        }
        catch (WarehouseException exception)
        {
            Error = exception.Code switch
            {
                ErrorCode.InvalidCredentials => "Неверный логин или пароль.",
                ErrorCode.AccountLocked => "Учётная запись заблокирована.",
                ErrorCode.Concurrency => "Попробуйте войти ещё раз.",
                _ => "Не удалось войти. Проверьте данные для входа."
            };
        }
        catch (Exception exception) when (exception is DbException or IOException or TimeoutException)
        {
            Error = "Не удалось подключиться к хранилищу.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
