namespace PharmaX.Desktop.Mvp;

public interface IView
{
    // Implemented by PhxFormBase; presenters call these and never touch
    // MessageBox or Cursor directly.
    void ShowError(string message);
    void ShowInfo(string message);
    bool Confirm(string question);

    /// A file was exported to path. PhxFormBase offers to open it.
    void ShowSaved(string path) => ShowInfo("Exported to " + path);

    void SetBusy(bool busy);
    void CloseView(bool success);
}

public interface IPresenter<in TView> where TView : IView
{
    void Attach(TView view);
    Task InitialiseAsync();
}
