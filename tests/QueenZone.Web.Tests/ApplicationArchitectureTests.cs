using System.Reflection;
using System.Xml.Linq;

namespace QueenZone.Web.Tests;

public sealed class ApplicationArchitectureTests
{
    [Fact]
    public void Application_and_its_project_dependencies_have_no_aspnet_references()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CheckProject(RepoPaths.Combine("src", "QueenZone.Application", "QueenZone.Application.csproj"), visited);
        Assert.NotEmpty(visited);

        var assemblies = new Queue<Assembly>();
        assemblies.Enqueue(typeof(MaintenanceJobRunner).Assembly);
        var checkedAssemblies = new HashSet<string>();
        while (assemblies.TryDequeue(out var assembly))
        {
            if (!checkedAssemblies.Add(assembly.FullName!))
            {
                continue;
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                Assert.False(IsAspNet(reference.Name!), $"{assembly.GetName().Name} references {reference.Name}");
                if (reference.Name!.StartsWith("QueenZone.", StringComparison.Ordinal))
                {
                    Assert.NotEqual("QueenZone.Web", reference.Name);
                    assemblies.Enqueue(Assembly.Load(reference));
                }
            }
        }
    }

    [Fact]
    public void Workers_do_not_depend_on_the_web_project()
    {
        var projects = Directory.GetFiles(RepoPaths.Combine("src"), "*.Worker.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            CheckProject(project, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void CheckProject(string path, HashSet<string> visited)
    {
        path = Path.GetFullPath(path);
        if (!visited.Add(path))
        {
            return;
        }

        Assert.NotEqual("QueenZone.Web.csproj", Path.GetFileName(path));
        var document = XDocument.Load(path);
        foreach (var item in document.Descendants().Where(element =>
            element.Name.LocalName is "FrameworkReference" or "PackageReference" or "Reference"))
        {
            var name = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update") ?? string.Empty;
            Assert.False(IsAspNet(name), $"{path} references {name}");
        }

        foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var relative = ((string)reference.Attribute("Include")!).Replace('\\', Path.DirectorySeparatorChar);
            CheckProject(Path.Combine(Path.GetDirectoryName(path)!, relative), visited);
        }
    }

    private static bool IsAspNet(string name) => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase);
}
