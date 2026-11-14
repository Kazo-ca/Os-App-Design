namespace Kazo.Design;

/// <summary>
/// The DesignCraft web build this app carries in <c>wwwroot/designcraft/</c>, described by its
/// provenance file <c>designcraft.json</c> (written by <c>tools/vendor-designcraft.sh</c>, checked by
/// the tests).
/// </summary>
public static class DesignCraftBuild
{
    /// <summary>
    /// The DesignCraft version of the build. Kept equal to the <c>version</c> of
    /// <c>wwwroot/designcraft/designcraft.json</c> by a test, so the window can show it without
    /// reading the file at run time.
    /// </summary>
    public const string Version = "0.20.1";

    /// <summary>Folder of the build under the app's static files (<c>_content/Kazo.Design/</c>).</summary>
    public const string Folder = "designcraft";
}
