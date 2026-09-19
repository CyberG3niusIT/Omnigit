using System.Collections.Generic;

namespace Omnigit.Localization;

/// <summary>
/// Cultures the localization layer is expected to support. A culture only becomes
/// selectable once a matching translation resource exists; this catalog defines the
/// intended baseline so the architecture does not grow around English-only assumptions.
/// </summary>
public static class SupportedCultures
{
    public static IReadOnlyList<string> All { get; } =
    [
        "en-US",
        "de-DE",
        "es-ES",
        "fr-FR",
        "pt-BR",
        "ja-JP",
        "zh-CN",
        "zh-TW",
        "ko-KR",
        "ru-RU",
        "it-IT",
        "nl-NL",
        "pl-PL",
        "tr-TR",
        "id-ID",
        "vi-VN",
        "ar-SA",
        "fa-IR",
        "hi-IN",
        "bn-BD",
        "uk-UA",
        "cs-CZ",
    ];

    /// <summary>
    /// Cultures with translation resources complete enough to expose in the UI.
    /// Add a culture here only when its resource set is ready for users.
    /// </summary>
    public static IReadOnlyList<string> Available { get; } =
    [
        "en-US",
        "de-DE",
    ];

    public static IReadOnlySet<string> RightToLeft { get; } =
        new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "ar-SA",
            "fa-IR",
        };
}
