using System.Text.Json;
using Api.Data;

namespace Api.Practice;

/// <summary>
/// One practice question. <see cref="Answer"/> and <see cref="Check"/> are keyed by
/// "default", "sqlserver", "mysql" or "postgres"; a dialect key overrides "default".
/// Write exercises (INSERT/UPDATE/DELETE) have a <see cref="Check"/> query that is run after the
/// statement, in the same rolled-back transaction, to see what it changed.
/// </summary>
public sealed record Exercise(
    int Id,
    string Category,
    string Difficulty,
    string Title,
    string Prompt,
    string? Hint,
    bool Ordered,
    Dictionary<string, string> Answer,
    Dictionary<string, string>? Check)
{
    public string AnswerFor(Db db) => Resolve(Answer, db)!;
    public string? CheckFor(Db db) => Check is null ? null : Resolve(Check, db);

    static string? Resolve(Dictionary<string, string> map, Db db) =>
        map.TryGetValue(db.ToString().ToLowerInvariant(), out var sql) ? sql : map.GetValueOrDefault("default");
}

public sealed class ExerciseCatalog
{
    public IReadOnlyList<Exercise> All { get; }
    readonly Dictionary<int, Exercise> _byId;

    public ExerciseCatalog(IHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Practice", "Exercises.json");
        All = JsonSerializer.Deserialize<List<Exercise>>(File.ReadAllText(path), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Exercises.json is empty");
        _byId = All.ToDictionary(e => e.Id);
    }

    public Exercise? Find(int id) => _byId.GetValueOrDefault(id);
}
