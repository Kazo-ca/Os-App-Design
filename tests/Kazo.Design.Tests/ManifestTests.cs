using Kazo.Os.App.Abstractions;

namespace Kazo.Design.Tests;

/// <summary>The app's manifest class, the source of the generated manifest.json (doc/en/plan.md, decision 1).</summary>
public sealed class ManifestTests
{
    private readonly DesignApp _app = new();

    [Fact]
    public void The_app_is_the_Kazo_Design_window_app()
    {
        Assert.Equal("ca.kazo.design", _app.Id);
        Assert.Equal("Kazo Design", _app.Name);
        Assert.Equal("pub.kazo", _app.PublisherId);
        Assert.Equal("graphics", _app.Category);
        Assert.Equal(AppKind.Window, _app.Kind);
        Assert.Equal(typeof(DesignWindow), _app.EntryPoint);
    }

    [Fact]
    public void Several_windows_can_run_at_once()
    {
        Assert.False(_app.SingleInstancePerUser);
    }

    [Fact]
    public void The_window_opens_at_1280_by_820_and_keeps_DesignCraft_minimum()
    {
        WindowOptions window = Assert.IsType<WindowOptions>(_app.Window);
        Assert.Equal(1280, window.DefaultWidth);
        Assert.Equal(820, window.DefaultHeight);
        // 900 × 560 of content (DesignCraft's desktop minimum), plus the borders and the title bar.
        Assert.Equal(902, window.MinWidth);
        Assert.Equal(600, window.MinHeight);
    }

    [Fact]
    public void The_window_stays_on_its_screen_because_moving_it_reloads_DesignCraft()
    {
        // Kazo OS ADR 0263, issue #2: another screen is another document, and the iframe reloads.
        WindowOptions window = Assert.IsType<WindowOptions>(_app.Window);
        Assert.False(window.CanChangeScreen);
    }

    [Fact]
    public void The_icon_is_declared_as_the_app_icon()
    {
        Assert.Equal(["icons/app.svg"], _app.Icons);
        AssetManifest icon = Assert.Single(_app.Assets);
        Assert.Equal("icons/app.svg", icon.Path);
        Assert.Equal("app-icon", icon.Role);
    }

    [Fact]
    public void The_app_asks_for_the_capabilities_of_Kazo_Photo()
    {
        Assert.Equal(
            [
                AppCapabilities.DialogFileOpen,
                AppCapabilities.DialogFileSave,
                AppCapabilities.FileSystemUserHomeRead,
                AppCapabilities.FileSystemUserHomeWrite,
                AppCapabilities.FileSystemPrivateRead,
                AppCapabilities.FileSystemPrivateWrite,
                AppCapabilities.AppsLaunch,
            ],
            _app.Capabilities);
    }

    [Fact]
    public void The_file_handlers_are_the_formats_of_decision_7()
    {
        Assert.Equal(Documents.DesignFormats.FileHandlers, _app.FileHandlers);
    }
}
