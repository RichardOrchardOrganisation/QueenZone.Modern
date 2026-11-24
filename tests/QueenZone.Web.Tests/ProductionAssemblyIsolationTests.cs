using Microsoft.AspNetCore.Authentication;

namespace QueenZone.Web.Tests;

public sealed class ProductionAssemblyIsolationTests
{
    [Fact]
    public void Local_support_cannot_be_replaced_after_explicit_registration()
    {
        Assert.Throws<ArgumentNullException>(() => LocalHostSupport.Register(null!));
        Assert.Throws<InvalidOperationException>(() => LocalHostSupport.Register(new TestingHostSupport()));
    }

    [Fact]
    public void Production_assembly_has_no_test_handlers_or_fixtures()
    {
        var production = typeof(Program).Assembly;
        Assert.NotEqual(production, typeof(TestAuthHandler).Assembly);
        Assert.NotEqual(production, typeof(TestMemberAuthHandler).Assembly);
        Assert.NotEqual(production, typeof(MobileApiContractHost).Assembly);
        Assert.NotEqual(production, typeof(CrosswordBrowserFixture).Assembly);
        Assert.NotEqual(production, typeof(MemberBrowserFixture).Assembly);
        Assert.DoesNotContain(production.GetReferencedAssemblies(),
            assembly => assembly.Name is "QueenZone.Testing" or "QueenZone.TestHost");
        Assert.DoesNotContain(production.GetTypes(), type =>
            typeof(IAuthenticationHandler).IsAssignableFrom(type)
            && type.Name.StartsWith("Test", StringComparison.Ordinal));
    }
}
