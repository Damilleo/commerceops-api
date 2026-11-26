using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommerceOps.Api.Tests;

public sealed class HealthEndpointsTests(TestApiFactory factory)
    : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task GetHealth_ReturnsOkAndHealthyStatus()
    {
        using var client = factory.CreateClient(TestApiFactory.HttpsOptions);
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetOpenApi_DocumentsHealthResponseInDevelopment()
    {
        using var developmentFactory = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment(Environments.Development));
        using var client = developmentFactory.CreateClient(TestApiFactory.HttpsOptions);
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty("/health").GetProperty("get");
        var content = operation.GetProperty("responses").GetProperty("200").GetProperty("content");
        Assert.True(content.TryGetProperty("application/json", out _));
    }

    [Fact]
    public async Task Production_RejectsPlainHttp()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));
        using var client = productionFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetOpenApi_ReturnsNotFoundInProduction()
    {
        using var productionFactory = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment(Environments.Production));
        using var client = productionFactory.CreateClient(TestApiFactory.HttpsOptions);
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
