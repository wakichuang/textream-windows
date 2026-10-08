using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TextreamWindows.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 開發診斷用：TEXTREAM_SOFTWARE_RENDER=1 不用顯示卡畫畫面（2026-10-08 查截圖全白時加的）
        if (Environment.GetEnvironmentVariable("TEXTREAM_SOFTWARE_RENDER") == "1")
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
        base.OnStartup(e);
    }
}
