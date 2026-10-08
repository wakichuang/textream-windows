namespace TextreamWindows.Core.Session;

/// <summary>
/// 講稿念的是哪種語言（瓦基 2026-10-08）。辨識模型都是 A（中英雙語），這裡切換的是比對規則：
/// 繁體中文照原本的規則（中文夾英文也可以）；English 用給純英文講稿調過的規則。
/// </summary>
public enum SpeechLanguage
{
    TraditionalChinese,
    English,
}
