using Xunit;

namespace MiniErp.Architecture.Tests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Backend_project_references_do_not_form_cycles()
    {
        var projectsByPath = ArchitectureProjectCatalog.BackendProjects
            .ToDictionary(project => project.FilePath, StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<string>();

        bool Visit(ArchitectureProject project)
        {
            if (completed.Contains(project.FilePath))
            {
                return true;
            }

            if (!visiting.Add(project.FilePath))
            {
                cycles.Add(project.RelativePath);
                return false;
            }

            var acyclic = true;
            foreach (var referencePath in project.ProjectReferences.Where(projectsByPath.ContainsKey))
            {
                acyclic &= Visit(projectsByPath[referencePath]);
            }

            visiting.Remove(project.FilePath);
            completed.Add(project.FilePath);
            return acyclic;
        }

        foreach (var project in projectsByPath.Values)
        {
            Visit(project);
        }

        Assert.True(cycles.Count == 0, $"Project reference cycles found: {string.Join(", ", cycles)}");
    }

    [Fact]
    public void Shared_technical_projects_do_not_depend_on_runtime_hosts()
    {
        var sharedProjects = ArchitectureProjectCatalog.BackendProjects
            .Where(project => project.Name is "MiniErp.Contracts" or "MiniErp.Application.Abstractions" or "MiniErp.Observability")
            .ToArray();
        var hostPaths = ArchitectureProjectCatalog.BackendProjects
            .Where(project => project.RelativePath.StartsWith("src/backend/hosts/", StringComparison.OrdinalIgnoreCase))
            .Select(project => project.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(sharedProjects);

        var violations = sharedProjects
            .SelectMany(project => project.ProjectReferences
                .Where(hostPaths.Contains)
                .Select(reference => $"{project.RelativePath} references {Path.GetFileName(reference)}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Application_projects_do_not_depend_on_hosts_or_infrastructure()
    {
        var projects = ArchitectureProjectCatalog.BackendProjects;
        var applicationProjects = projects
            .Where(project => project.RelativePath.StartsWith("src/backend/application/", StringComparison.OrdinalIgnoreCase)
                || project.Name.StartsWith("MiniErp.Application", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var forbiddenPaths = projects
            .Where(project => project.RelativePath.StartsWith("src/backend/hosts/", StringComparison.OrdinalIgnoreCase)
                || project.RelativePath.StartsWith("src/backend/infrastructure/", StringComparison.OrdinalIgnoreCase))
            .Select(project => project.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(applicationProjects);

        var violations = applicationProjects
            .SelectMany(project => project.ProjectReferences
                .Where(forbiddenPaths.Contains)
                .Select(reference => $"{project.RelativePath} references forbidden project {Path.GetFileName(reference)}"))
            .ToArray();

        Assert.Empty(violations);
    }
}
