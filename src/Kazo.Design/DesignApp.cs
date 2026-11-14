using Kazo.Design.Documents;
using Kazo.Os.App.Abstractions;

namespace Kazo.Design;

/// <summary>
/// The app's manifest (doc/en/plan.md, decision 1). The build generates <c>manifest.json</c> from
/// this class (Kazo.Os.App.Build); it is never instantiated at run time.
/// </summary>
/// <remarks>
/// Work item 1 declares the identity, the window and its size, several windows at once, a window that
/// stays on its screen, the capabilities of Kazo Photo and the file associations of decision 7
/// (<see cref="DesignFormats"/>). Opening and saving those files come with work item 2, on
/// DesignCraft's host mode (fork, F2).
/// </remarks>
[AppManifest]
public sealed class DesignApp : AppDefinition
{
    /// <summary>
    /// Smallest usable width of DesignCraft, in pixels: its desktop minimum (tools, page, the
    /// Properties panel).
    /// </summary>
    public const int ContentMinWidth = 900;

    /// <summary>Smallest usable height of DesignCraft, in pixels: its desktop minimum.</summary>
    public const int ContentMinHeight = 560;

    /// <inheritdoc />
    public override string Id => "ca.kazo.design";

    /// <inheritdoc />
    public override string Name => "Kazo Design";

    /// <inheritdoc />
    public override AppKind Kind => AppKind.Window;

    /// <inheritdoc />
    public override Type EntryPoint => typeof(DesignWindow);

    /// <inheritdoc />
    public override string PublisherId => "pub.kazo";

    /// <inheritdoc />
    public override string Description => "A page layout app for brochures, magazines, posters and books, based on DesignCraft by the ArtCraft team";

    /// <inheritdoc />
    /// <remarks>The category of Kazo Photo and Kazo Fig (decision 1).</remarks>
    public override string Category => "graphics";

    /// <inheritdoc />
    /// <remarks>The tag that publishes to the Kazo OS Store must be v + this version.</remarks>
    public override string Version => "0.1.0";

    /// <inheritdoc />
    /// <remarks>
    /// Several windows at once: each one runs its own DesignCraft in its own iframe, because the
    /// DesignCraft canvas id is fixed and allows one instance per HTML document (doc/en/plan.md).
    /// </remarks>
    public override bool SingleInstancePerUser => false;

    /// <inheritdoc />
    /// <remarks>
    /// Kazo Photo's list: the OS Open and Save dialogs, the user's files, the app's private folder
    /// (per-user settings, should DesignCraft ever store any; decision 11), and launching a new window
    /// of the app (a double click in the File Explorer opens a new window, decision 6).
    /// </remarks>
    public override IReadOnlyList<string> Capabilities =>
    [
        AppCapabilities.DialogFileOpen,
        AppCapabilities.DialogFileSave,
        AppCapabilities.FileSystemUserHomeRead,
        AppCapabilities.FileSystemUserHomeWrite,
        AppCapabilities.FileSystemPrivateRead,
        AppCapabilities.FileSystemPrivateWrite,
        AppCapabilities.AppsLaunch,
    ];

    /// <inheritdoc />
    /// <remarks>Default app (<c>open</c> and <c>edit</c>) of what nothing else opens (decision 7).</remarks>
    public override IReadOnlyList<FileHandler> FileHandlers => DesignFormats.FileHandlers;

    /// <inheritdoc />
    public override IReadOnlyList<string> Icons => ["icons/app.svg"];

    /// <inheritdoc />
    public override IReadOnlyList<AssetManifest> Assets =>
        [new("icons/app.svg", AssetKind.Image, new string('0', 64), "app-icon")];

    /// <inheritdoc />
    /// <remarks>
    /// Sizes are the outer size of the OS window (title bar included): 1280 × 820 by default, which
    /// DesignCraft's tools, page and Properties panel fill (doc/en/plan.md, "Trial"); at least
    /// DesignCraft's minimum plus the frame. <see cref="WindowOptions.CanChangeScreen"/> is
    /// <see langword="false"/>: moving the window to another screen moves its element into another
    /// document, which reloads DesignCraft's iframe and loses the open documents (Kazo OS ADR 0263,
    /// issue #2).
    /// </remarks>
    public override WindowOptions? Window => new()
    {
        DefaultWidth = 1280,
        DefaultHeight = 820,
        MinWidth = ContentMinWidth + WindowChromeWidth,
        MinHeight = ContentMinHeight + WindowChromeHeight,
        CanChangeScreen = false,
    };

    // Borders and title bar of an OS window (the tallest title bar of the Kazo OS themes, 2.35rem).
    private const int WindowChromeWidth = 2;
    private const int WindowChromeHeight = 40;
}
