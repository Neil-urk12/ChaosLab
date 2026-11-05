using Backend.Features.Auth;
using Backend.Features.Projects;
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

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    if (!await roles.RoleExistsAsync(AuthSetup.SuperAdminRole))
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BootstrapSetupOptions>>().Value;
        var token = app.Services.GetRequiredService<BootstrapSetupToken>();
        // The fragment keeps the secret out of frontend HTTP request URLs and ordinary request logs.
        Console.WriteLine($"Complete first-run SuperAdmin setup (expires in one hour): {token.SetupUrl(options.PublicUrl)}");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapProjectEndpoints();

app.Run();

// Expose the top-level entry point to WebApplicationFactory integration tests.
public partial class Program { }
