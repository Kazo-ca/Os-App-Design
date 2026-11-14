using System.Globalization;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Design.Hosting;

/// <summary>
/// The language given to DesignCraft by its command <c>app.language { lang }</c> (decision 10).
/// </summary>
/// <remarks>
/// DesignCraft's languages are compiled in (<c>crates/ui-egui/src/i18n/</c>): English and the 15
/// codes of <see cref="Available"/>. English is the empty code, so <see cref="CommandValue"/> turns
/// the module's <see cref="CraftLanguage.English"/> into <c>""</c>.
/// </remarks>
public static class DesignCraftLanguage
{
    /// <summary>The languages DesignCraft 0.20.1 has, besides English.</summary>
    public static IReadOnlyList<string> Available { get; } =
    [
        "de", "fr", "es", "ja", "zh", "ar", "pt-br", "it", "uk", "ru", "cs", "pl", "ka", "hu", "sv",
    ];

    /// <summary>The code for a culture: one of <see cref="Available"/>, or <see cref="CraftLanguage.English"/>.</summary>
    /// <param name="culture">The Kazo user's culture.</param>
    /// <returns>A code DesignCraft has, or <c>en</c>.</returns>
    public static string Of(CultureInfo culture) => CraftLanguage.Pick(culture, (IReadOnlyCollection<string>)Available);

    /// <summary>The value of <c>app.language</c>'s <c>lang</c> for a culture (English is <c>""</c>).</summary>
    /// <param name="culture">The Kazo user's culture.</param>
    /// <returns>A code of <see cref="Available"/>, or the empty string.</returns>
    public static string CommandValue(CultureInfo culture)
    {
        string code = Of(culture);
        return code == CraftLanguage.English ? string.Empty : code;
    }
}
