using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Scheduler.Application;
using Scheduler.Domain;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ARCH01_Domain_has_no_outward_assembly_dependencies()
    {
        var references = typeof(BookingRules).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => IsOuterDependency(r.Name!));
        Assert.DoesNotContain(references, r => r.Name!.StartsWith("Scheduler."));
    }

    [Fact]
    public void ARCH02_Application_depends_only_on_domain_and_platform()
    {
        var references = typeof(BookingRequest).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => IsOuterDependency(r.Name!));
        Assert.DoesNotContain(references, r => r.Name!.StartsWith("Scheduler.") && r.Name != "Scheduler.Domain");
    }

    [Theory]
    [InlineData("Scheduler.Domain")]
    [InlineData("Scheduler.Application")]
    public void ARCH01_02_Project_references_do_not_hide_unused_outer_dependencies(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Keyloop.ServiceScheduler.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory!.FullName, "src", "backend", project, project + ".csproj"));
        var packages = document.Descendants("PackageReference").Select(e => (string)e.Attribute("Include")!);
        Assert.DoesNotContain(packages, IsOuterDependency);
        var projects = document.Descendants("ProjectReference").Select(e => Path.GetFileNameWithoutExtension((string)e.Attribute("Include")!));
        if (project == "Scheduler.Domain") Assert.Empty(projects);
        else Assert.All(projects, p => Assert.Equal("Scheduler.Domain", p));
        Assert.DoesNotContain(document.Descendants("FrameworkReference"), e => ((string?)e.Attribute("Include")) == "Microsoft.AspNetCore.App");
    }

    [Fact]
    public void ARCH02_Controllers_inject_application_handlers_without_storage_dependencies()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)).ToArray();
        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            var dependencies = controller.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToArray();
            Assert.Contains(dependencies, t => t.Assembly == typeof(BookingRequest).Assembly);
            var fields = controller.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType);
            Assert.DoesNotContain(dependencies.Concat(fields), t => IsStorageType(t));
        }
    }

    private static bool IsStorageType(Type type) =>
        type.Assembly.GetName().Name!.StartsWith("Scheduler.Infrastructure") ||
        type.Assembly.GetName().Name!.StartsWith("Microsoft.EntityFrameworkCore") ||
        type.Assembly.GetName().Name!.StartsWith("Npgsql") ||
        type.GetGenericArguments().Any(IsStorageType);

    private static bool IsOuterDependency(string name) =>
        name.StartsWith("Microsoft.EntityFrameworkCore") || name.StartsWith("Npgsql") ||
        name.StartsWith("Microsoft.AspNetCore") || name.StartsWith("Scheduler.Infrastructure") ||
        name.StartsWith("Scheduler.Api");
}
