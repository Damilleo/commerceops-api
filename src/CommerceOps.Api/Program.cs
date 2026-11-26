using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommerceOps.Api.Auth;
using CommerceOps.Api.Commands;
using CommerceOps.Api.Endpoints;
using CommerceOps.Application.Catalog;
using CommerceOps.Application.Inventory;
using CommerceOps.Infrastructure.Catalog;
using CommerceOps.Infrastructure.Demo;
using CommerceOps.Infrastructure.Inventory;
using Microsoft.EntityFrameworkCore;

var command = args is ["bootstrap-admin"] or ["seed-demo"] ? args[0] : null;
var builder = WebApplication.CreateBuilder(command is null ? args : []);
builder.Services.AddCommerceOpsAuth(builder.Configuration);

builder.Services.AddOpenApi(options => options.AddBearerDocumentation());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RequestExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<StockMovementType>(JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<IInventoryStore, EfInventoryStore>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ICatalogStore, EfCatalogStore>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<DemoCatalogSeeder>();
}

builder.Services.AddDbContext<CatalogDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Catalog");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Configure ConnectionStrings:Catalog before using catalog endpoints.");
    }

    options.UseSqlServer(connectionString);
});

await using var app = builder.Build();

if (command == "bootstrap-admin")
{
    return await AdminBootstrap.RunCommandAsync(app.Services);
}

if (command == "seed-demo")
{
    return await SeedDemoCommand.RunAsync(app.Environment, app.Services);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    var localDevelopment = app.Environment.IsDevelopment() &&
        context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    if (!context.Request.IsHttps && !localDevelopment)
    {
        await Results.Problem(statusCode: 400, detail: "HTTPS is required.").ExecuteAsync(context);
        return;
    }

    await next(context);
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoint();
var api = app.MapGroup("/api/v1");
api.MapCatalogEndpoints();
api.MapInventoryEndpoints();
api.MapAuthEndpoints();

await app.RunAsync();
return 0;

public partial class Program
{
}
