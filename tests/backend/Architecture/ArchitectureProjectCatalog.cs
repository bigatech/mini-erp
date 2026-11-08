using System.Xml.Linq;

namespace MiniErp.Architecture.Tests;

internal sealed record ArchitectureProject(
    string FilePath,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences)
{
    public string RelativePath => Path.GetRelativePath(ArchitectureProjectCatalog.RepositoryRoot, FilePath)
        .Replace(Path.DirectorySeparatorChar, '/');

    public string Name => Path.GetFileNameWithoutExtension(FilePath);
}

internal static class ArchitectureProjectCatalog
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static IReadOnlyList<ArchitectureProject> BackendProjects { get; } = LoadBackendProjects();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MiniErp.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate MiniErp.sln from the test output directory.");
    }

    private static IReadOnlyList<ArchitectureProject> LoadBackendProjects()
    {
        var backendRoot = Path.Combine(RepositoryRoot, "src", "backend");

        return Directory.GetFiles(backendRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(LoadProject)
            .ToArray();
    }

    private static ArchitectureProject LoadProject(string filePath)
    {
        var projectDirectory = Path.GetDirectoryName(filePath)!;
        var document = XDocument.Load(filePath);
        var projectReferences = document.Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
            .ToArray();
        var packageReferences = document.Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();

        return new ArchitectureProject(filePath, projectReferences, packageReferences);
    }
}
