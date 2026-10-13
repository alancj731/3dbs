using System.Threading.RateLimiting;
using Api;
using Api.Data;
using Api.Practice;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DbRegistry>();
builder.Services.AddSingleton<Seeder>();
builder.Services.AddSingleton<ExerciseCatalog>();
builder.Services.AddSingleton<Runner>();
builder.Services.AddSingleton<Checker>();
builder.Services.AddHostedService<Warmup>();

// The Angular app (on Vercel) calls the API from another origin (Cors:AllowedOrigins).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).WithMethods("GET", "POST").AllowAnyHeader()));

// Anyone can submit SQL, so limit how much each visitor (and everyone together) can run.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per visitor: runs/checks a minute (RateLimit:SqlPerMinute, default 60).
    var perMinute = builder.Configuration.GetValue("RateLimit:SqlPerMinute", 60);
    o.AddPolicy(Endpoints.SqlPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));

    // Whole server: at most 8 SQL requests at once, the rest wait in a short queue.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Method == HttpMethods.Post
            ? RateLimitPartition.GetConcurrencyLimiter("sql", _ => new ConcurrencyLimiterOptions { PermitLimit = 8, QueueLimit = 32 })
            : RateLimitPartition.GetNoLimiter("other"));
});

var app = builder.Build();

// Tests start the app without databases, so seeding can be switched off.
if (app.Configuration.GetValue("Seed:OnStartup", true))
    await app.Services.GetRequiredService<Seeder>().SeedAsync();

app.UseCors();
app.UseRateLimiter();
app.MapPracticeApi();

app.Run();

// In production Caddy always sets X-Real-IP to the visitor's address (overwriting any value the client sent).
// The other fallbacks are for local dev and tests.
static string ClientKey(HttpContext ctx) =>
    ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
    ?? ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
    ?? ctx.Connection.RemoteIpAddress?.ToString()
    ?? "unknown";

// Lets the tests start the app with WebApplicationFactory<Program>.
public partial class Program;
