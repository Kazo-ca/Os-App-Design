using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Design.Documents;

/// <summary>
/// The files Kazo Design opens (doc/en/plan.md, decision 7): DesignCraft's own documents and the
/// InDesign files it converts when it opens them. Nothing else in Kazo OS opens them, so Kazo Design
/// is their default app.
/// </summary>
/// <remarks>
/// <para>
/// The formats DesignCraft only imports into a document (images, <c>.docx</c>, <c>.pdf</c>, <c>.xlsx</c>
/// and the others of File › Place) get no association: they are placed, not opened.
/// </para>
/// <para>
/// Work item 1 only declares the associations. Which saves are the document and which are copies
/// (<see cref="CraftDocumentFormat.IsCopy"/>) is decided by DesignCraft itself in host mode (fork,
/// H3: only the document's own save is a <c>save</c> message, exports stay downloads), so no format
/// is marked as a copy here.
/// </para>
/// </remarks>
public static class DesignFormats
{
    /// <summary>DesignCraft's own document format, the only one a document is saved in.</summary>
    public const string NativeExtension = ".designcraft";

    /// <summary>Gets every format, with what Kazo Design does with it.</summary>
    public static CraftDocumentFormats Formats { get; } = new(
    [
        Default(NativeExtension, "application/x-designcraft"),
        // InDesign Markup Language (IANA type), and InDesign documents and templates: converted when
        // opened, saved as .designcraft (decision 8).
        Default(".idml", "application/vnd.adobe.indesign-idml-package"),
        Default(".indd", "application/x-indesign"),
        Default(".indt", "application/x-indesign-template"),
    ]);

    /// <summary>Gets the extensions the OS Open dialog proposes, in the order above.</summary>
    public static IReadOnlyList<string> OpenExtensions => Formats.OpenExtensions;

    /// <summary>Gets the file associations of the manifest.</summary>
    public static IReadOnlyList<FileHandler> FileHandlers => Formats.FileHandlers;

    private static CraftDocumentFormat Default(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: true, IsCopy: false);
}
