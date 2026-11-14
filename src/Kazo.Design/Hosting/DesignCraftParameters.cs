namespace Kazo.Design.Hosting;

/// <summary>The query of DesignCraft's iframe page (<c>CraftFrameOptions.Parameters</c>).</summary>
public static class DesignCraftParameters
{
    /// <summary>
    /// The parameters for one window:
    /// <list type="bullet">
    /// <item><c>control</c>: DesignCraft installs <c>window.designcraft</c>, the command API the
    /// module's proxy drives it with, only when the query asks for it (fork, H7);</item>
    /// <item><c>webgl</c>: forces the WebGL 2 renderer, which DesignCraft otherwise only uses when the
    /// browser has no WebGPU.</item>
    /// </list>
    /// Bare flags are empty values: DesignCraft only looks for the name.
    /// </summary>
    /// <param name="webGl">Whether to force WebGL 2.</param>
    /// <returns>The parameters, in a stable order.</returns>
    public static IReadOnlyDictionary<string, string> For(bool webGl)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal) { ["control"] = string.Empty };
        if (webGl)
        {
            parameters["webgl"] = string.Empty;
        }

        return parameters;
    }
}
