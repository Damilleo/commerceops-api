using CommerceOps.Api.Endpoints;
using CommerceOps.Application.Catalog;
using CommerceOps.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CatalogExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
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
app.MapGroup("/api/v1").MapCatalogEndpoints();

app.Run();

public partial class Program
{
}
