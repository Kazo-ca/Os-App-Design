namespace Kazo.Design.Tests;

/// <summary>Where the repository is, found from the test binaries.</summary>
internal static class Repository
{
    /// <summary>Gets the folder that holds <c>Kazo.Design.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kazo.Design.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Kazo.Design.slnx not found above " + AppContext.BaseDirectory);
    }
}
