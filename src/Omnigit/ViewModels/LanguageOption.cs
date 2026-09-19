namespace Omnigit.ViewModels;

/// <summary>
/// One selectable UI language. A null culture name means follow the operating system.
/// Display names for real cultures are deliberately shown in their own language.
/// </summary>
public sealed record LanguageOption(string? CultureName, string DisplayName);
