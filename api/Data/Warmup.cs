using System.Diagnostics;
using Api.Practice;

namespace Api.Data;

/// <summary>
/// Keeps the first Check after a quiet period fast. Connection pools drop idle connections after a few minutes
/// (a new login costs a TLS handshake per database), and cold plan/buffer caches make the first run of each
/// query slow. So at startup every reference answer is run once on every database (also JIT-compiling the
/// hot paths), and afterwards each database is pinged often enough to keep a pooled connection alive.
/// Switched off with Warmup:Enabled=false (the tests have no databases).
/// </summary>
public sealed class Warmup(Runner runner, ExerciseCatalog catalog, IConfiguration config, ILogger<Warmup> log) : BackgroundService
{
    static readonly TimeSpan PingInterval = TimeSpan.FromMinutes(2);
    static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Warmup:Enabled", true)) return;
        await Task.Yield(); // don't hold up startup

        try
        {
            await RunLoopAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // A failing warm-up only costs speed; it must never take the API down with it.
            log.LogWarning(ex, "Warm-up stopped");
        }
    }

    async Task RunLoopAsync(CancellationToken ct)
    {
        await RunAnswersAsync(ct);
        var lastRefresh = DateTime.UtcNow;

        using var timer = new PeriodicTimer(PingInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (DateTime.UtcNow - lastRefresh >= RefreshInterval)
            {
                await RunAnswersAsync(ct);
                lastRefresh = DateTime.UtcNow;
            }
            else
            {
                foreach (var db in Enum.GetValues<Db>())
                    await runner.RunAsync(db, "SELECT 1", null, 1, ct);
            }
        }
    }

    async Task RunAnswersAsync(CancellationToken ct)
    {
        foreach (var db in Enum.GetValues<Db>())
        {
            var sw = Stopwatch.StartNew();
            var failed = 0;
            foreach (var ex in catalog.All)
            {
                var outcome = await runner.RunAsync(db, ex.AnswerFor(db), ex.CheckFor(db), Runner.CompareRows, ct);
                if (outcome.Error is not null) failed++;
            }
            log.LogInformation("{Db}: warmed up {Count} answers in {Ms} ms ({Failed} failed)", db, catalog.All.Count, sw.ElapsedMilliseconds, failed);
        }
    }
}
