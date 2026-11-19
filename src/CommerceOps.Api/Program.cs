using System.Text.Json;
using System.Text.Json.Serialization;
using CommerceOps.Api.Endpoints;
using CommerceOps.Application.Catalog;
using CommerceOps.Application.Inventory;
using CommerceOps.Infrastructure.Catalog;
using CommerceOps.Infrastructure.Inventory;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
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
builder.Services.AddDbContext<CatalogDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Catalog");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Configure ConnectionStrings:Catalog before using catalog endpoints.");
    }

    options.UseSqlServer(connectionString);
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoint();
var api = app.MapGroup("/api/v1");
api.MapCatalogEndpoints();
api.MapInventoryEndpoints();

app.Run();

public partial class Program
{
}
