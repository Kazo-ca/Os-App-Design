namespace Kazo.Design.Hosting;

/// <summary>
/// The DesignCraft interface brightness (<c>ui.set { brightness }</c>) for a Kazo OS colour scheme
/// (decision 10).
/// </summary>
public static class DesignCraftTheme
{
    /// <summary>DesignCraft's darkest brightness.</summary>
    public const string Dark = "dark";

    /// <summary>DesignCraft's lightest brightness.</summary>
    public const string Light = "light";

    /// <summary>
    /// <see cref="Light"/> for the OS's light scheme, <see cref="Dark"/> for anything else (the OS
    /// default is dark).
    /// </summary>
    /// <param name="scheme">The scheme <c>CraftFrame</c> reports (<c>light</c> or <c>dark</c>).</param>
    /// <returns>The value of DesignCraft's <c>brightness</c> setting.</returns>
    public static string Of(string? scheme) =>
        string.Equals(scheme, "light", StringComparison.OrdinalIgnoreCase) ? Light : Dark;
}
