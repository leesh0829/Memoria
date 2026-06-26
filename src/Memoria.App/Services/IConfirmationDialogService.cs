namespace Memoria.App.Services;

public interface IConfirmationDialogService
{
    /// <summary>사용자가 확인(예)을 누르면 true, 취소(아니오)면 false.</summary>
    bool Confirm(string message);
}
