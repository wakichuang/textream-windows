using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TextreamWindows.App.Mvvm;

/// <summary>ViewModel 的基底：屬性變了通知畫面。自己寫幾行就夠，不另外裝 MVVM 套件。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
