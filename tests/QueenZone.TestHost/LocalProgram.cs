using QueenZone.Web;

namespace QueenZone.TestHost;

public static class LocalProgram
{
    public static async Task Main(string[] args)
    {
        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")))
        {
            var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../src/QueenZone.Web"));
            if (Directory.Exists(webRoot))
                Directory.SetCurrentDirectory(webRoot);
        }
        LocalHostSupport.Register(new TestingHostSupport());
        await Program.RunAsync(args);
    }
}
