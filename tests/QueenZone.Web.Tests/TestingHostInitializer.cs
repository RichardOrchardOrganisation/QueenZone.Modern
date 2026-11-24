using System.Runtime.CompilerServices;

namespace QueenZone.Web.Tests;

internal static class TestingHostInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => LocalHostSupport.Register(new TestingHostSupport());
}
