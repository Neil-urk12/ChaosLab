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
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.Configure<BootstrapAdminOptions>(
    builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.AddScoped<IInitialAdminBootstrapper, InitialAdminBootstrapper>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapper>().BootstrapAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

var api = app.MapGroup("/api");
api.MapProjectEndpoints();

app.Run();

// Expose the top-level entry point to WebApplicationFactory integration tests.
public partial class Program { }
