using Xunit;

namespace MiniErp.Architecture.Tests;

public sealed class DomainIndependenceTests
{
    private static readonly string[] ForbiddenProjectAreas =
    [
        "src/backend/application/",
        "src/backend/hosts/",
        "src/backend/infrastructure/",
        "src/backend/observability/",
        "tests/"
    ];

    private static readonly string[] ForbiddenPackagePrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "AWSSDK.",
        "Azure.Storage.",
        "Minio",
        "Microsoft.Extensions.Http",
        "System.Net.Http",
        "Microsoft.UI",
        "Avalonia",
        "Microsoft.Maui"
    ];

    [Fact]
    public void Domain_projects_do_not_depend_on_frameworks_or_infrastructure()
    {
        var domainProjects = ArchitectureProjectCatalog.BackendProjects
            .Where(IsDomainProject)
            .ToArray();

        var violations = domainProjects.SelectMany(project =>
        {
            var projectReferenceViolations = project.ProjectReferences
                .Select(reference => ArchitectureProjectCatalog.BackendProjects
                    .FirstOrDefault(candidate => string.Equals(candidate.FilePath, reference, StringComparison.OrdinalIgnoreCase)))
                .Where(reference => reference is not null && ForbiddenProjectAreas.Any(area =>
                    reference.RelativePath.StartsWith(area, StringComparison.OrdinalIgnoreCase)))
                .Select(reference => $"{project.RelativePath} references forbidden project {reference!.RelativePath}");

            var packageViolations = project.PackageReferences
                .Where(package => ForbiddenPackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(package => $"{project.RelativePath} references forbidden package {package}");

            return projectReferenceViolations.Concat(packageViolations);
        }).ToArray();

        Assert.Empty(violations);
    }

    private static bool IsDomainProject(ArchitectureProject project) =>
        project.RelativePath.Contains("/Domain/", StringComparison.OrdinalIgnoreCase)
        || project.Name.EndsWith(".Domain", StringComparison.OrdinalIgnoreCase);
}
