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
/// Which saves are the document and which are copies
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

    /// <summary>
    /// Says whether DesignCraft's file picker is File › Open (or the start screen's Open…), which the
    /// window answers itself with the OS Open dialog (work item 2): DesignCraft's web build asks for
    /// <c>.designcraft</c>, <c>.idml</c>, <c>.indd</c> and <c>.indt</c> there. Load Styles asks for
    /// <c>.designcraft</c> and <c>.idml</c> only (fork, branch <c>kazo/2c-styles-picker</c>), Place for
    /// images, the Swatches panel for <c>.ase</c>: those get the chosen file's bytes.
    /// </summary>
    /// <param name="request">DesignCraft's file picker.</param>
    /// <returns><see langword="true"/> for File › Open.</returns>
    public static bool IsOpenPicker(PickRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.AcceptsAny([NativeExtension]) && request.AcceptsAny([".indd"]) && request.AcceptsOnly(OpenExtensions);
    }

    private static CraftDocumentFormat Default(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: true, IsCopy: false);
}
