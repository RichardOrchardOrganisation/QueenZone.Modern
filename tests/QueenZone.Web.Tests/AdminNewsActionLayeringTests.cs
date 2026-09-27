using System.Reflection;
using QueenZone.Data;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.NewsDiscovery;
using SuggestionActionModel = QueenZone.Web.Pages.Admin.NewsSuggestions.ActionModel;

namespace QueenZone.Web.Tests;

public sealed class AdminNewsActionLayeringTests
{
    [Fact]
    public void DiscoveryAndSuggestionActionModels_DoNotTakeServiceProviderOrDbContext()
    {
        AssertConstructorUsesWriteService(typeof(ActionModel), typeof(AdminNewsWriteService));
        AssertConstructorUsesWriteService(typeof(SuggestionActionModel), typeof(NewsSuggestionService));
    }

    [Fact]
    public void PageModels_DoNotDependOnDbContextEntityFrameworkOrServiceProvider()
    {
        var pageModels = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("QueenZone.Web.Pages", StringComparison.Ordinal) == true
                && typeof(Microsoft.AspNetCore.Mvc.RazorPages.PageModel).IsAssignableFrom(type))
            .ToList();
        Assert.NotEmpty(pageModels);

        foreach (var pageModel in pageModels)
        {
            var dependencies = pageModel
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(ctor => ctor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Concat(pageModel
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(field => field.FieldType));

            foreach (var dependency in dependencies)
            {
                Assert.False(
                    dependency == typeof(QueenZoneDbContext)
                        || dependency == typeof(IServiceProvider)
                        || dependency.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true,
                    $"{pageModel.FullName} depends on {dependency.FullName}; page models go through services.");
            }
        }
    }

    private static void AssertConstructorUsesWriteService(Type pageModel, Type writeService)
    {
        var ctor = Assert.Single(pageModel.GetConstructors());
        var parameterTypes = ctor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.DoesNotContain(typeof(IServiceProvider), parameterTypes);
        Assert.DoesNotContain(typeof(QueenZoneDbContext), parameterTypes);
        Assert.Contains(writeService, parameterTypes);
    }
}
