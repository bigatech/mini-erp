using Xunit;

namespace MiniErp.Architecture.Tests;

public sealed class HostDependencyTests
{
    [Fact]
    public void Hosts_do_not_reference_other_hosts_or_domain_projects_directly()
    {
        var projects = ArchitectureProjectCatalog.BackendProjects;
        var hostProjects = projects
            .Where(project => project.RelativePath.StartsWith("src/backend/hosts/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hostPaths = hostProjects.Select(project => project.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var domainPaths = projects.Where(project =>
                project.RelativePath.Contains("/Domain/", StringComparison.OrdinalIgnoreCase)
                || project.Name.EndsWith(".Domain", StringComparison.OrdinalIgnoreCase))
            .Select(project => project.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(hostProjects);

        var violations = hostProjects.SelectMany(host => host.ProjectReferences
                .Where(reference => hostPaths.Contains(reference) || domainPaths.Contains(reference))
                .Select(reference => $"{host.RelativePath} directly references {Path.GetFileNameWithoutExtension(reference)}"))
            .ToArray();

        Assert.Empty(violations);
    }
}
