using System.Text.Json;

namespace Kazo.Design.Documents;

/// <summary>
/// One document DesignCraft has open, as its <c>document.list</c> answer lists it (fork, H7):
/// <c>[{ index, uid, title, path, dirty, active }]</c>.
/// </summary>
/// <param name="Uid">
/// Names the document while it is open: unlike <paramref name="Index"/>, it does not change when a
/// tab before it closes. The window binds each tab to its file by this id (doc/en/plan.md,
/// decision 6).
/// </param>
/// <param name="Index">The tab's place, which <c>file.activate { index }</c> takes.</param>
/// <param name="Title">The name on the tab.</param>
/// <param name="Path">
/// The name DesignCraft saves the document under: the file's name for a <c>.designcraft</c> the window
/// handed over (H2), the name of the last confirmed save otherwise, <see langword="null"/> for a new
/// or converted document never saved.
/// </param>
/// <param name="Dirty">Whether the document has changes not yet in a file.</param>
/// <param name="Active">Whether the tab is in front.</param>
public sealed record DesignTab(long Uid, int Index, string Title, string? Path, bool Dirty, bool Active)
{
    /// <summary>Reads the tabs of a <c>document.list</c> answer.</summary>
    /// <param name="list">DesignCraft's answer. It comes from the iframe: anything malformed is skipped.</param>
    /// <returns>One entry per tab, in DesignCraft's order; empty when the answer is not a list.</returns>
    public static IReadOnlyList<DesignTab> ListFrom(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<DesignTab> tabs = [];
        foreach (JsonElement document in list.EnumerateArray())
        {
            if (document.ValueKind != JsonValueKind.Object
                || !TryGetLong(document, "uid", out long uid)
                || !TryGetLong(document, "index", out long index)
                || index is < 0 or > int.MaxValue
                || tabs.Any(tab => tab.Uid == uid))
            {
                continue;
            }

            string? path = Text(document, "path");
            tabs.Add(new DesignTab(
                uid,
                (int)index,
                Text(document, "title") ?? string.Empty,
                string.IsNullOrEmpty(path) ? null : path,
                Flag(document, "dirty"),
                Flag(document, "active")));
        }

        return tabs;
    }

    private static bool TryGetLong(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement number)
            && number.ValueKind == JsonValueKind.Number
            && number.TryGetInt64(out value);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
