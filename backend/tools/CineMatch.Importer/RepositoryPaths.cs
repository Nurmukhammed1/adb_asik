namespace CineMatch.Importer;

public static class RepositoryPaths
{
    /// <summary>
    /// The repository root (the folder with docker-compose.yml), found by walking up from the
    /// program's own folder. Lets the tools use the same data and results folders wherever they
    /// are started from.
    /// </summary>
    public static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) return dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>Resolves a configured path against the repository root unless it is already absolute.</summary>
    public static string Resolve(string path) => Path.GetFullPath(path, FindRoot());
}
