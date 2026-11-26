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
        Error = Status = "";
        try { action(); }
        catch (WarehouseException exception)
        {
            Error = exception.Code switch
            {
                ErrorCode.Forbidden => "У вашей роли нет прав для этого действия.",
                ErrorCode.InventoryLocked => "Позицию блокирует незавершённая ревизия.",
                ErrorCode.InsufficientStock => "На складе недостаточно товара.",
                ErrorCode.Concurrency => "Данные изменились. Повторите действие.",
                _ => exception.Message
            };
        }
        catch (Exception exception) when (exception is IOException or DbException or TimeoutException or UnauthorizedAccessException)
        {
            Error = "Не удалось прочитать или сохранить данные.";
        }
    }
}
