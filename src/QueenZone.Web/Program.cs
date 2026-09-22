using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using QueenZone.Web;
using QueenZone.Web.Health;
using QueenZone.Web.Sitemap;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

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
else if (QueenZoneEnvironments.IsAutomatedTestHost(builder.Environment))
{
    if (builder.Environment.IsEnvironment(QueenZoneEnvironments.E2E))
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

DataProtectionBootstrap.ConfigureServices(
    builder.Services,
    builder.Configuration,
    builder.Environment);

builder.Services.AddQueenZoneApplicationInsights(
    builder.Configuration,
    builder.Environment,
    builder.Logging);

builder.Services.AddQueenZoneWebComposition(builder.Configuration, builder.Environment);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", AdminAuthenticationSchemes.Policy);
    // Abuse-sensitive public/member surfaces (process-local rate limits; single B1 instance).
    // EnableRateLimitingAttribute is endpoint metadata, not an MVC filter.
    options.Conventions.AddFolderApplicationModelConvention(
        "/Submit",
        model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
            QueenZoneRateLimitPolicies.AuthenticatedWrite)));
    options.Conventions.AddPageApplicationModelConvention(
        "/Account/ExternalLogin",
        model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
            QueenZoneRateLimitPolicies.Auth)));
    options.Conventions.AddPageApplicationModelConvention(
        "/Account/Login",
        model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
            QueenZoneRateLimitPolicies.Auth)));
    options.Conventions.AddPageApplicationModelConvention(
        "/Account/Settings",
        model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
            QueenZoneRateLimitPolicies.AuthenticatedWrite)));
    options.Conventions.AddPageApplicationModelConvention(
        "/Search",
        model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
            QueenZoneRateLimitPolicies.Search)));

    foreach (var page in new[]
    {
        "/Index",
        "/Account/Delete",
        "/Account/MySubmissions",
        "/Forum/Block",
        "/Forum/EditPost",
        "/Forum/HideAuthor",
        "/Forum/NewThread",
        "/Forum/Report",
        "/Forum/Topic",
        "/Forum/TopicPage",
        "/Members/Profile",
        "/Quizzes/Play",
    })
    {
        options.Conventions.AddPageApplicationModelConvention(
            page,
            model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
                QueenZoneRateLimitPolicies.AuthenticatedWrite)));
    }

    options.Conventions.AddFolderApplicationModelConvention(
        "/Messages",
        model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
            QueenZoneRateLimitPolicies.AuthenticatedWrite)));

    foreach (var page in new[] { "/Help/Index", "/Quizzes/Sprint" })
    {
        options.Conventions.AddPageApplicationModelConvention(
            page,
            model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute(
                QueenZoneRateLimitPolicies.AnonymousWrite)));
    }
});

var app = builder.Build();

// Registered before literally everything else. Mapped endpoints only execute at the
// position of the implicit UseEndpoints() — always the very end of the pipeline,
// regardless of where Map* is called in source — so wrapping MapQueenZoneHealthEndpoints
// in a UseWhen branch further down (as done for the authenticated pipeline) cannot make
// probe paths skip earlier middleware like UseForwardedHeaders or UseStaticFiles. Only a
// short-circuiting middleware here can. #666's second cold-start hang happened even after
// probe paths were excluded from the authenticated pipeline, on requests that reached
// "Now listening" and ran a background query fine, then went silent for the platform's
// full startup-probe timeout — consistent with something earlier in the pipeline (a
// leading static-file existence check against the then-read-only, zip-mounted wwwroot
// under WEBSITE_RUN_FROM_PACKAGE, since removed, was the leading suspect) blocking
// before the health endpoint's own handler ever ran. Keeping the short-circuit here
// regardless: it is still the cheapest, most robust way to guarantee probe paths never
// wait on anything later in the pipeline.
app.Use(async (context, next) =>
{
    if (await QueenZoneHealthEndpoints.TryHandleProbeAsync(context))
    {
        return;
    }

    await next();
});

// Azure App Service (and CDN/proxy, e.g. Cloudflare) terminates TLS and forwards plain HTTP.
// Without forwarded headers, OAuth redirect_uri values use the internal host/scheme.
// KnownIPNetworks/Proxies are cleared (edge IP is not fixed). Trust boundary: App Service/
// Cloudflare as only public ingress. IP-based rate limits are soft if the edge is bypassed.
// See docs/architecture/azure-hosting-plan.md (forwarded-headers trust).
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);
// ASP.NET Core's automatic AllowedHosts filter runs outside this visible pipeline,
// before the probe short-circuit above. App Service startup pings use an internal
// link-local Host header, so that automatic filter returned 400 before /health could
// answer and WEBSITE_WARMUP_STATUSES=200 crash-looped the container (#684). The
// framework filter is deliberately disabled with AllowedHosts="*"; normal traffic is
// validated here, after probe paths have returned and forwarded Host is restored.
app.UseQueenZoneHostFiltering();
// Azure Monitor uses client.address for request geolocation, then masks the raw IP by
// default. The origin restriction makes Cloudflare's connecting-IP header trustworthy.
app.UseClientAddressTelemetry();
app.UseCanonicalHostRedirect();

if (ResponseCompressionBootstrap.IsEnabled(app.Environment))
{
    app.UseResponseCompression();
}

app.UseApiV1ExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseWhen(
        static context => !ApiV1.IsApiPath(context.Request.Path),
        branch => branch.UseExceptionHandler("/error"));
}

// PhysicalFileProvider excludes dot-prefixed files/folders by default, so the generic
// UseStaticFiles() below would 404 on /.well-known/* (used for Microsoft's domain
// association file, and any future well-known files like ACME challenges). Map it
// explicitly with exclusion filtering turned off before the catch-all static handler.
// WebRootPath can be null if no physical wwwroot folder is found relative to whatever
// the process's working directory happens to be at startup (e.g. a misconfigured launch
// script) â€” guard rather than crash the whole app over an optional route.
var wellKnownPath = string.IsNullOrEmpty(app.Environment.WebRootPath)
    ? null
    : Path.Combine(app.Environment.WebRootPath, ".well-known");
if (wellKnownPath is not null && Directory.Exists(wellKnownPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wellKnownPath, ExclusionFilters.None),
        RequestPath = "/.well-known",
    });
}

if (!app.Environment.IsDevelopment() && !QueenZoneEnvironments.IsAutomatedTestHost(app.Environment))
{
    app.UseHsts();
}

app.UseWhen(
    context => !QueenZoneHealthEndpoints.IsProbePath(context.Request.Path),
    branch => branch.UseHttpsRedirection());

app.Use(async (context, next) =>
{
    SecurityHeaders.Apply(context);
    await next();
});

app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseApiV1StatusCodePages();
var staticFileContentTypeProvider = new FileExtensionContentTypeProvider();
staticFileContentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticFileContentTypeProvider,
    OnPrepareResponse = ctx => StaticFileCacheControl.Apply(ctx.Context, app.Environment),
});
// Probe paths (/health, /health/ready, /warmup) skip authentication, authorization,
// rate limiting, output caching, and antiforgery entirely. These are Azure's own
// container startup gate and the deploy workflow's warmup poll — they must stay
// genuinely cheap. Routing every request through the default OpenID Connect scheme's
// AuthenticateAsync here, even for a liveness probe, risked a cold-container hang if
// that handler touched Entra metadata retrieval on first use; a probe stuck behind it
// blocked the platform's startup gate for its full timeout regardless of which probe
// path was configured (#666).
app.UseWhen(
    context => !QueenZoneHealthEndpoints.IsProbePath(context.Request.Path),
    branch =>
    {
        // Diagnostic marker: proves at runtime which requests actually entered the
        // full authenticated pipeline vs the probe fast-path (also exercised directly
        // by HealthEndpointsTests to guard against #666 regressing).
        branch.Use(async (context, next) =>
        {
            context.Response.Headers["X-QueenZone-Pipeline"] = "full";
            await next();
        });
        branch.UseAuthentication();
        // Public pages use a non-member default scheme; without this, HttpContext.User stays
        // anonymous while the MembersCookie is present. Antiforgery tokens then fail on member-only
        // APIs (e.g. editor image upload) because generation and validation see different identities.
        branch.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var member = await context.AuthenticateAsync(MemberAuthenticationSchemes.MembersCookie);
                if (member.Succeeded && member.Principal?.Identity?.IsAuthenticated == true)
                {
                    context.User = member.Principal;
                }
            }

            await next();
        });
        // After member-cookie fallback so User is populated; before authorization and
        // rate limiting so those warnings inherit TraceId / MemberId. Probe paths never
        // enter this UseWhen branch (#666); the middleware also no-ops IsProbePath.
        branch.UseRequestLogScope();
        branch.UseAuthorization();
        // Short browser/CDN Cache-Control for anonymous public HTML (after auth so User is known).
        // OnStarting runs when the response starts so Content-Type and status are available.
        branch.Use(async (context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                PublicHtmlCacheControl.TryApply((HttpContext)state!);
                return Task.CompletedTask;
            }, context);
            await next();
        });
        branch.UseRateLimiter();
        branch.UseOutputCache();
        branch.UseAntiforgery();
    });

// Liveness (/health) + readiness (/health/ready) — see QueenZoneHealthEndpoints.
app.MapQueenZoneHealthEndpoints();

// Member-gated fan-performance audio downloads use FanPerformanceEndpoints.
app.MapGet("/account/member-probe", () => Results.Ok(new { authenticated = true }))
    .RequireAuthorization(MemberAuthenticationSchemes.MemberPolicy);

app.MapFanPerformanceEndpoints();
app.MapAdminFanPerformanceSubmissionEndpoints();
app.MapForumAttachmentEndpoints();
app.MapForumPollEndpoints();
app.MapEditorImageUploadEndpoints();
app.MapMemberAvatarEndpoints();
app.MapUgcProxyEndpoints();
app.MapSitemapEndpoints();
app.MapArticleAutosaveEndpoint();
app.MapArticlesFeedEndpoint();
app.MapQueenZoneApiV1();
app.MapAdminApiEndpoints();
app.MapMobileAuthEndpoints();
app.MapContentApiEndpoints();
app.MapSearchApiEndpoints();
app.MapForumApiEndpoints();
app.MapContactApiEndpoints();
app.MapMeApiEndpoints();
app.MapFanPerformanceReportApiEndpoints();
app.MapMessagesApiEndpoints();
app.MapSubmissionsApiEndpoints();
app.MapMemberApiEndpoints();
app.MapDevicesApiEndpoints();
app.MapNotificationPreferencesApiEndpoints();
// Anonymous public HTML is output-cached (short TTL); policy no-ops for authenticated users
// and for the Testing environment so integration suites stay deterministic.
app.MapRazorPages().CacheOutput(PublicOutputCachePolicies.PublicHtml);
app.MapFallbackToPage("/NotFound");

app.Run();

public partial class Program;

