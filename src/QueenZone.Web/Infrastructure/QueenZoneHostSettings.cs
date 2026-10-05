namespace QueenZone.Web;

public static class QueenZoneHostSettings
{
    public static void Configure(WebApplicationBuilder builder)
    {
        // Local secrets only in Development. Loading them for Production/Staging would let a
        // developer machine's empty AzureAd:ClientId override App Service settings when
        // ASPNETCORE_ENVIRONMENT is mis-set, and would break production-shaped integration tests.
        // Development WebApplicationFactory hosts (testhost) skip Local.json so a workstation's
        // optional Analytics secrets cannot fail-closed an unrelated test.
        if (QueenZoneDevelopmentHost.ShouldLoadLocalSettings(
                builder.Environment,
                builder.Configuration,
                QueenZoneDevelopmentHost.GetEntryAssemblyName()))
        {
            builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            // Keep environment variables above Local.json so CI/shell overrides still win.
            builder.Configuration.AddEnvironmentVariables();
        }
        else if (builder.Environment.IsDevelopment())
        {
            QueenZoneDevelopmentHost.NeutralizeIncompleteAnalytics(builder.Configuration);
        }
        else if (QueenZoneEnvironments.IsAutomatedTestHost(builder.Environment)
            && builder.Environment.IsEnvironment(QueenZoneEnvironments.E2E))
        {
            // appsettings.E2E.json ships the "admin@test.local" default; E2E_ADMIN_EMAIL lets the
            // nightly runner override it with a single env var instead of the nested
            // Admin__AllowedEmails__0 binding syntax.
            var e2eAdminEmail = Environment.GetEnvironmentVariable("E2E_ADMIN_EMAIL");
            if (!string.IsNullOrWhiteSpace(e2eAdminEmail))
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Admin:AllowedEmails:0"] = e2eAdminEmail,
                });
            }
        }
    }
}
