using System.Windows.Input;

namespace TextreamWindows.App.Mvvm;

/// <summary>按鈕綁的指令。能不能按由 <see cref="CanExecute"/> 決定，狀態變了呼叫 <see cref="Refresh"/>。</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
