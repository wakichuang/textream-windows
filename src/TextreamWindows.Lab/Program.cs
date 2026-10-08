// TextreamWindows.Lab：開發用的主控台工具（計畫書第 2 階段；第 4.3 步加 follow）。

using TextreamWindows.Lab;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Usage = """
    TextreamWindows.Lab：語音引擎的量測工具

    用法：TextreamWindows.Lab <指令> [參數]

      devices                              列出麥克風（* 是 Windows 預設）
      record <檔名.wav> [--seconds N]       錄成 16 kHz 單聲道 16-bit WAV；不給秒數就錄到 Ctrl+C
                        [--device X] [--force]
      transcribe <模型> <音檔.wav>          辨識一個檔案，印出結果與每個字的時間
      live <模型> [--device X] [--seconds N] 對著麥克風即時辨識，邊講邊印
      bench <模型> <音檔.wav>               照真實速度播放，量延遲、CPU、RTF
                        [--fast] [--chunk-ms 50] [--json 結果.json]
                        [--from 秒] [--to 秒]   只量其中一段
      follow <講稿.txt>                     對著麥克風念稿，即時顯示讀到哪裡（預設模型 A 的 fp32）
                        [--device X] [--seconds N] [--raw]  --raw 另外顯示辨識原文（簡體）
                        [--record 錄音.wav] [--log 紀錄.json] 存下這次的錄音與過程，覺得怪時拿來重現
                        [--wav 音檔] [--speed 1]  改播錄音檔，不開麥克風
                        [--model A] [--int8]
                        [--lang zh|en]            辨識語言，跟主視窗的選項一樣（預設 zh）

    <模型> 是代號 A／B／C／D，或模型資料夾路徑。模型用 scripts/fetch_models.py 下載。
    音檔可以是 WAV 或 MP3（MP3 用 Windows 內建的 Media Foundation 解碼）。
    共用選項：--threads N（預設 2）、--fp32（優先用 fp32 模型檔，預設 int8）
    --device 給 devices 列出的編號或名稱片段，例如 --device Focusrite
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return 0;
}

try
{
    var rest = args.Skip(1);
    return args[0] switch
    {
        "devices" => Commands.Devices(new CommandLine(rest)),
        "record" => Commands.Record(new CommandLine(rest, "force")),
        "transcribe" => Commands.Transcribe(new CommandLine(rest, "fp32")),
        "live" => Commands.Live(new CommandLine(rest, "fp32")),
        "bench" => Commands.Bench(new CommandLine(rest, "fp32", "fast")),
        "follow" => FollowCommand.Run(new CommandLine(rest, "int8", "raw")),
        _ => throw new ArgumentException($"未知的指令：{args[0]}（用 --help 看清單）"),
    };
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
