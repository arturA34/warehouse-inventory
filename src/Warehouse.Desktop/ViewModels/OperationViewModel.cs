using System.Data.Common;
using Warehouse.Domain;

namespace Warehouse.Desktop.ViewModels;

public abstract class OperationViewModel : ViewModel
{
    private string error = "";
    private string status = "";

    public string Error { get => error; private set => Set(ref error, value); }
    public string Status { get => status; protected set => Set(ref status, value); }

    protected void Run(Action action)
    {
        ClearFeedback();
        try { action(); }
        catch (WarehouseException exception) { ShowError(exception); }
        catch (Exception exception) when (IsDataError(exception)) { ShowDataError(); }
    }

    protected void ClearFeedback() => Error = Status = "";

    protected void ShowError(WarehouseException exception) => Error = exception.Code switch
    {
        ErrorCode.Forbidden => "У вашей роли нет прав для этого действия.",
        ErrorCode.InventoryLocked => "Позицию блокирует незавершённая ревизия.",
        ErrorCode.InsufficientStock => "На складе недостаточно товара.",
        ErrorCode.Concurrency => "Данные изменились. Повторите действие.",
        _ => exception.Message
    };

    protected static bool IsDataError(Exception exception) =>
        exception is IOException or DbException or TimeoutException or UnauthorizedAccessException;

    protected void ShowDataError() => Error = "Не удалось прочитать или сохранить данные.";
}
