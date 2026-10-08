# Textream for Windows

會「聽你講到哪裡」的提詞機。講稿浮在螢幕上方，你照稿念，黃色高亮就跟著你的聲音往下走；
講錯、跳句、插話都會自己接回來。語音辨識完全在你的電腦上進行，**不用網路、不會上傳任何聲音**。

移植自 Mac 版的 [Textream](https://github.com/f/textream)（作者 Fatih Kadir Akin，MIT 授權），
由 [瓦基（閱讀前哨站）](https://readingoutpost.com) 改成 Windows 版，加強了中文：用拼音比對，簡繁、同音字都追得上。

## 下載與安裝（免安裝版）

1. 到 [**Releases 下載頁**](https://github.com/wakichuang/textream-windows/releases/latest) 下載 `Textream-for-Windows-…-win-x64.zip`（約 380 MB，內含語音模型，不用另外下載任何東西）。
2. 在 zip 上按右鍵 →「解壓縮全部」。**不要直接在 zip 裡點兩下執行**。
3. 打開解壓縮出來的「Textream for Windows」資料夾，雙擊 `TextreamWindows.exe`。
   - 如果跳出藍色的「Windows 已保護您的電腦」：按「其他資訊」→「仍要執行」。這是因為程式是個人免費分享、沒有買數位簽章。
   - 想從開始功能表打開：在 `TextreamWindows.exe` 上按右鍵 →「釘選到開始畫面」。

需要 Windows 10（2004 版）或 Windows 11、64 位元，一支麥克風。不用另外安裝 .NET。

## 怎麼用

1. 把講稿貼進中間的大框框，或按「開啟…」選 `.txt`、`.md` 檔。
2. 模式選「逐字追蹤」，按「開始」（或在任何程式裡按 `Ctrl+Alt+L`）。
3. 照稿念。講稿浮在螢幕頂端（也可以選浮動視窗或全螢幕），黃色會一次標 2～4 個字跟著你走，下方顯示剛剛聽到的話。
4. 按 `Esc` 停止。停下來的地方會記住；回到編輯畫面調一調稿子，再按「開始」就從游標的位置接著念。

| 快捷鍵（任何程式裡都能按） | 作用 |
| :-- | :-- |
| `Ctrl+Alt+L` | 開始／暫停；讀完時是「重來」 |
| `Ctrl+Alt+↑` ／ `↓` | 跳到上一句／下一句 |
| `Esc` | 停止（浮層在畫面上時） |
| 在浮層上點任何一個字 | 從那個字開始 |
| 在浮層上轉滾輪 | 一格跳一行 |

**三種模式**：逐字追蹤（聽你念到哪就跟到哪）、定速捲動（照設定的每秒幾字一直往前）、有講話才捲（照速度往前，但只在你講話時前進）。

**辨識語言**：逐字追蹤時可以選「繁體中文」或「English」。中文講稿（中間夾英文也可以）選繁體中文；整份英文的講稿選 English，比對規則會換成適合英文的版本（語音模型同一個，本來就聽得懂中英文）。

**錄影、分享畫面**：右上角「錄影／分享畫面」預設是「看不到浮層」，錄影、截圖、Google Meet 分享畫面都看不到提詞，只有你看得到；
想錄示範影片時改成「錄得到浮層」。

**外觀**：跟著 Windows 的深淺色，也可以固定淺色或深色。字級、浮層大小、速度都會記住。

### 沒有反應？

- **聽不到聲音**：Windows「設定 → 隱私權與安全性 → 麥克風」，打開「讓桌面應用程式存取您的麥克風」，再到 Textream 的「麥克風」選單選對裝置。
- **說找不到語音模型**：確認 `models` 資料夾跟 `TextreamWindows.exe` 在同一個資料夾裡，沒有被搬走或刪掉。

## 給開發者

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)（`winget install Microsoft.DotNet.SDK.10`）與 Python 3。開發規則見 [`AGENTS.md`](AGENTS.md)。

```powershell
python scripts/fetch_models.py A          # 下載語音模型 A 到 %LOCALAPPDATA%\Textream\models（約 500 MB）
dotnet test TextreamWindows.slnx          # 全部測試
dotnet run --project src/TextreamWindows.App
python scripts/publish.py                 # 打包免安裝版 zip 到 publish/
```

| 專案 | 用途 |
| :-- | :-- |
| `src/TextreamWindows.Core` | 純邏輯：斷字、拼音、數字正規化、對齊（含跳句、插話的容錯）、VAD、模式狀態機 |
| `src/TextreamWindows.Speech` | sherpa-onnx 包裝、麥克風、WAV／MP3 回放 |
| `src/TextreamWindows.App` | WPF 介面：編輯器、三種浮層、快捷鍵、錄影時隱藏 |
| `src/TextreamWindows.Lab` | 開發用主控台工具：列麥克風、錄音、辨識、量延遲、`follow` 試念 |
| `tests/*` | 對應的 xUnit 測試；`Desktop.Tests` 需要真的 Windows 桌面，只在本機跑 |

`scripts/fetch_models.py --list` 可以看其他候選模型（B、C、D），比較準確度與延遲用 `TextreamWindows.Lab bench`。

## 授權

本專案以 [MIT 授權](LICENSE) 公開，歡迎使用、修改、再分享。
用到的第三方程式、資料與語音模型（Textream、sherpa-onnx、ONNX Runtime、NAudio、Unicode Unihan、語音模型 A）及其授權見 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。
