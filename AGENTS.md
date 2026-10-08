# AGENTS.md：Textream for Windows（語音追蹤提詞機）

> 給 AI 協作者（Codex、Claude Code 等）與想動手改程式的人看的開發規則。Claude Code 由同資料夾的 `CLAUDE.md` 用一行 `@AGENTS.md` 匯入。
> 維護者本人的計畫書與進度表放在私人工作區，不在這個 repo。

## 一句話

Windows 版的 Textream：講稿浮在螢幕上，用離線語音辨識（sherpa-onnx 串流模型）聽你講到哪裡，高亮跟著聲音走。
C# + .NET 10 + WPF，MIT 授權，免費公開分享。

## 目錄地圖

```text
textream-windows/
├── src/
│   ├── TextreamWindows.Core/      純邏輯（net10.0），不准碰 Windows API
│   ├── TextreamWindows.Speech/    sherpa-onnx、麥克風、WAV 回放
│   ├── TextreamWindows.App/       WPF 介面（net10.0-windows）
│   └── TextreamWindows.Lab/       開發用主控台工具
├── tests/
│   ├── TextreamWindows.Core.Tests/     TDD 主戰場，CI 跑
│   ├── TextreamWindows.Speech.Tests/   模型範例音檔 CI 跑；黃金錄音只在有錄音的電腦上跑
│   └── TextreamWindows.Desktop.Tests/  視窗行為，只在 Windows 本機跑
├── data/                     拼音表等純文字資料（可進版控）
└── scripts/                  Python 工具（下載模型、產生拼音表、做圖示、打包免安裝版）
```

## 開發紀律

1. **TDD**：先寫測試、跑 `dotnet test` 看它因為對的理由失敗（不是編譯錯誤），再寫剛好讓它通過的程式，最後整理。一個紅綠循環 commit 一次，commit 訊息寫清楚加了哪條規格。
2. **不准為了讓測試通過而改測試的預期值。** 真的是預期值寫錯（或規格改了），要在 commit 訊息講清楚為什麼改。
3. **commit 前 `dotnet test TextreamWindows.slnx` 要全綠。** 建置設定是警告即錯誤（`Directory.Build.props`），不要用 `#pragma` 或改設定把警告壓掉。每次跑完，完整結果會存在 `TestResults/<測試專案>.trx`；遇到偶發失敗、終端機輸出又被截斷時，先看這份找出是哪個測試。
4. **`TextreamWindows.Core` 不准引用任何 Windows 專屬 API 或 NuGet 套件**，它要能在 CI 與任何平台上單獨測。
5. **字數一律用文字元素（grapheme）計算**，不要用 `string.Length`。Swift 的 `Character` 天生是文字元素，照抄 Textream 的索引會在 emoji 與罕用字錯位。
6. 套件版本集中鎖定（`Directory.Packages.props`），升級 sherpa-onnx 時要跑完整測試（含黃金錄音回放）。
7. 新增第三方套件、資料或素材時，在 `THIRD_PARTY_NOTICES.md` 補一節（來源、版本、用途、授權）。

## 不進 repo 的東西

| 東西 | 放哪 |
| :-- | :-- |
| 語音模型（`*.onnx`，每個 100～700 MB） | 開發時 `%LOCALAPPDATA%\Textream\models\`（`python scripts/fetch_models.py`）；免安裝版放在執行檔旁的 `models\` |
| 黃金錄音與它們的講稿（`*.wav`、`*.md`） | 維護者自己的資料夾，測試用環境變數 `TEXTREAM_GOLDEN_DIR` 找它，找不到就略過 |
| 使用者設定 | `%APPDATA%\Textream\settings.json` |
| 打包結果 | `publish/`（`python scripts/publish.py`），zip 上傳到 GitHub Releases |

`.gitignore` 已擋 `*.onnx`、`*.wav`、`/models/`、`publish/`。commit 前看一眼 `git status`，有二進位大檔或個人資料就停下來。**這是公開 repo，推上去就是公開的。**

## Windows 上的坑

- 主要 shell 是 **PowerShell 5.1**。腳本一律用 Python 寫（指令是 `python`，**不是 `python3`**，後者在很多 Windows 上是微軟商店的假殼）。
- 有些電腦**沒開長路徑**，路徑超過 260 字元會失敗。
- 在 Claude 桌面版裡跑的程式，寫進 `AppData` 的檔案會被轉存到 Claude 的私人資料夾，自己雙擊開的程式看不到。要給一般使用者用的檔案不要靠這條路放。

## 發佈

1. `dotnet test TextreamWindows.slnx` 全綠，版本號在 `src/TextreamWindows.App/TextreamWindows.App.csproj` 的 `<Version>`。
2. `python scripts/publish.py` 產生 `publish/Textream-for-Windows-<版本>-win-x64.zip`（單一執行檔＋模型 A＋授權檔＋使用說明）。
3. 在一台乾淨的 Windows 上解壓縮試一次，再上傳到 GitHub Releases。

## 回報

- 一律繁體中文，用四種狀態開頭（✅ 做完了／⚠️ 做完了但有疑慮／❓ 資訊不夠／⛔ 卡住了），附證據（測試數字、量測結果）。
- 維護者說「推上去」才推。只用 `main` 一條分支，不 force push。
