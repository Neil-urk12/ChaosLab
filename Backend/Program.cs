using Backend.Features.ApiSpecs;
using Backend.Features.Auth;
using Backend.Features.Environments;
using Backend.Features.Scenarios;
using Backend.Infrastructure.Security;
using Backend.Features.Projects;
using Backend.Features.TestRuns;
using Backend.Features.Findings;
using Backend.Features.Members;
using Backend.Features.Reports;
using Backend.Features.Users;
using Backend.Infrastructure.Data;
using Backend.Domain.Entities;
using Backend.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

// Local dev only: load .env.local if present; NoClobber keeps real env vars (pod secret) authoritative.
DotNetEnv.Env.NoClobber().Load(".env.local");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.")));

builder.Services.AddIdentityCore<User>()
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();
builder.Services.AddAppAuthentication();
builder.Services.AddOptions<BootstrapSetupOptions>()
    .Bind(builder.Configuration.GetSection(BootstrapSetupOptions.SectionName))
    .Validate(options => BootstrapSetupOptions.IsValidPublicUrl(options.PublicUrl),
        "BootstrapSetup:PublicUrl must be an absolute HTTP(S) URL without a query, fragment or credentials.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BootstrapSetupToken>();
builder.Services.AddHttpClient(ImportSpec.FetchClient, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.MaxResponseContentBufferSize = SpecDocument.MaxBytes;
}).ConfigurePrimaryHttpMessageHandler(ImportSpec.CreateFetchHandler);
builder.Services.AddTargetClients();
builder.Services.AddSingleton<CredentialProtector>();
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton<DependencySimulator>();
builder.Services.AddHostedService<RunExecutor>();
builder.Services.AddSingleton<ReportStorage>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    // On for image installs (compose.yaml), which have no SDK for `make migrate`; off in development. Runs before the
    // run executor starts with the host.
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    if (!await roles.RoleExistsAsync(AuthSetup.SuperAdminRole))
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BootstrapSetupOptions>>().Value;
        var token = app.Services.GetRequiredService<BootstrapSetupToken>();
        // ANSI colors only in an interactive terminal; pod logs and NO_COLOR get plain text.
        var color = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
        string Paint(string code, string text) => color ? $"\e[{code}m{text}\e[0m" : text;
        var rule = Paint("2", new string('─', 64));
        // The fragment keeps the secret out of frontend HTTP request URLs and ordinary request logs.
        Console.WriteLine($"""

            {rule}
              {Paint("1;33", "First-run setup")}{Paint("2", " · create the SuperAdmin account")}

              {Paint("4;36", token.SetupUrl(options.PublicUrl))}

              {Paint("2", "Expires in one hour. Restart the backend for a new link.")}
            {rule}

            """);
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// One summary line per request. The path only: a query string can carry tokens, and a simulator path is cut short
// because its token is the route's only key.
app.Use(async (context, next) =>
{
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    await next(context);
    var path = context.Request.Path.StartsWithSegments("/api/simulator") ? "/api/simulator/…" : context.Request.Path.Value;
    app.Logger.LogInformation("{Method} {Path} {StatusCode} {ElapsedMs:0}ms", context.Request.Method, path,
        context.Response.StatusCode, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
});
app.UseHttpsRedirection();
// The built web app in wwwroot (only in the image; development uses Vite). Served before authentication, so no sign-in.
// Default files serve "/", which the fallback's catch-all below does not match.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapProjectEndpoints();
api.MapApiSpecEndpoints();
api.MapScenarioEndpoints();
api.MapEnvironmentEndpoints();
api.MapTestRunEndpoints();
api.MapFindingEndpoints();
api.MapReportEndpoints();
api.MapMemberEndpoints();
api.MapUserEndpoints();

// Every page path outside /api gets the app's index.html, which routes in the browser; unknown /api paths stay 404.
app.MapFallbackToFile("{*path:regex(^(?!api(/|$))):nonfile}", "index.html").AllowAnonymous();

app.Run();

// Expose the top-level entry point to WebApplicationFactory integration tests.
public partial class Program { }
