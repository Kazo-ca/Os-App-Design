using System.Globalization;
using Kazo.Design.Hosting;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Design.Tests;

/// <summary>What the window gives DesignCraft: the page's query, the language, the theme and the chords.</summary>
public sealed class HostingTests
{
    [Fact]
    public void The_page_always_asks_DesignCraft_for_its_command_API()
    {
        // DesignCraft installs window.designcraft only with ?control (fork, H7); the module's proxy needs it.
        Assert.Equal(new Dictionary<string, string> { ["control"] = string.Empty }, DesignCraftParameters.For(webGl: false));
    }

    [Fact]
    public void The_webgl_argument_forces_WebGL_2()
    {
        IReadOnlyDictionary<string, string> parameters = DesignCraftParameters.For(webGl: true);

        Assert.Equal(string.Empty, parameters["webgl"]);
        Assert.Equal(string.Empty, parameters["control"]);
    }

    [Theory]
    [InlineData("fr-CA", "fr")]
    [InlineData("de-DE", "de")]
    [InlineData("pt-PT", "pt-br")]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh-TW", "zh")]
    [InlineData("ka-GE", "ka")]
    [InlineData("sv-SE", "sv")]
    [InlineData("en-US", "en")]
    [InlineData("nl-NL", "en")]
    public void DesignCraft_gets_one_of_its_15_languages_or_English(string culture, string expected)
    {
        Assert.Equal(expected, DesignCraftLanguage.Of(CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData("en-US", "")]
    [InlineData("nl-NL", "")]
    [InlineData("fr-FR", "fr")]
    public void English_is_the_empty_code_of_app_language(string culture, string expected)
    {
        Assert.Equal(expected, DesignCraftLanguage.CommandValue(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void DesignCraft_has_15_languages_besides_English()
    {
        Assert.Equal(15, DesignCraftLanguage.Available.Count);
        Assert.Equal(DesignCraftLanguage.Available.Count, DesignCraftLanguage.Available.Distinct().Count());
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData("LIGHT", "light")]
    [InlineData("dark", "dark")]
    [InlineData(null, "dark")]
    [InlineData("high-contrast", "dark")]
    public void DesignCraft_follows_the_OS_scheme_not_the_browser(string? scheme, string expected)
    {
        Assert.Equal(expected, DesignCraftTheme.Of(scheme));
    }

    [Fact]
    public void Ctrl_N_stays_DesignCraft_s_New_document()
    {
        // Decision 12: the window reserves the OS chords only.
        string window = File.ReadAllText(Path.Combine(Repository.Root, "src", "Kazo.Design", "DesignWindow.razor.cs"));
        Assert.Contains("ReservedKeys = ReservedKeys.Os,", window);
        Assert.DoesNotContain(ReservedKeys.NewWindow, ReservedKeys.Os);
    }
}
