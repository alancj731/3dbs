using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests;

/// <summary>Starts the real API in memory, without databases: seeding and warm-up are switched off,
/// and only requests that are answered before a connection is opened are tested.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Paths.Api);
        builder.UseSetting("Seed:OnStartup", "false");
        builder.UseSetting("Warmup:Enabled", "false");
    }
}

public class EndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Lists_exercises_without_their_answers()
    {
        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/exercises");
        Assert.NotNull(list);
        Assert.True(list.Length >= 50);
        Assert.All(list, e => Assert.Contains(e.GetProperty("kind").GetString(), new[] { "read", "write" }));
        Assert.All(list, e => Assert.False(e.TryGetProperty("answer", out _)));
    }

    [Fact]
    public async Task Returns_the_answer_for_a_database()
    {
        var answer = await client.GetFromJsonAsync<JsonElement>("/api/sqlserver/exercises/1/answer");
        Assert.Contains("SELECT", answer.GetProperty("sql").GetString());
    }

    [Theory]
    [InlineData("/api/sqlserver/exercises/99999/answer")]
    [InlineData("/api/oracle/exercises/1/answer")]
    [InlineData("/api/oracle/schema")]
    public async Task Unknown_database_or_exercise_is_not_found(string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("COMMIT", "isn't allowed")]
    [InlineData("DROP TABLE SalesLT.Product", "Only SELECT")]
    [InlineData("   ", "Enter a SQL statement")]
    public async Task Refused_sql_is_reported_without_running(string sql, string error)
    {
        var response = await client.PostAsJsonAsync("/api/mysql/run", new { sql });
        response.EnsureSuccessStatusCode();
        var outcome = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(error, outcome.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Overlong_sql_is_refused()
    {
        var response = await client.PostAsJsonAsync("/api/postgres/exercises/1/check", new { sql = "SELECT " + new string('1', 20_001) });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("correct").GetBoolean());
        Assert.Contains("too long", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Each_visitor_is_rate_limited()
    {
        // The default limit is 60 SQL requests a minute per visitor; refused SQL still counts.
        HttpStatusCode last = default;
        for (var i = 0; i <= 60; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sqlserver/run") { Content = JsonContent.Create(new { sql = "COMMIT" }) };
            request.Headers.Add("X-Real-IP", "203.0.113.7");
            last = (await client.SendAsync(request)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);

        // Another visitor is unaffected.
        using var other = new HttpRequestMessage(HttpMethod.Post, "/api/sqlserver/run") { Content = JsonContent.Create(new { sql = "COMMIT" }) };
        other.Headers.Add("X-Real-IP", "203.0.113.8");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(other)).StatusCode);
    }
}
