using Api.Data;
using Api.Practice;
using Microsoft.Extensions.Hosting.Internal;

namespace Api.Tests;

public class ExerciseTests
{
    static readonly ExerciseCatalog Catalog = new(new HostingEnvironment { ContentRootPath = Paths.Api });

    [Fact]
    public void A_dialect_answer_overrides_the_default()
    {
        var ex = new Exercise(1, "c", "easy", "t", "p", null, false,
            new() { ["default"] = "SELECT TOP 5 x", ["mysql"] = "SELECT x LIMIT 5", ["postgres"] = "SELECT x LIMIT 5" },
            new() { ["default"] = "SELECT 1" });
        Assert.Equal("SELECT TOP 5 x", ex.AnswerFor(Db.SqlServer));
        Assert.Equal("SELECT x LIMIT 5", ex.AnswerFor(Db.MySql));
        Assert.Equal("SELECT 1", ex.CheckFor(Db.Postgres));
    }

    [Fact]
    public void Catalog_has_at_least_fifty_exercises_with_unique_ids()
    {
        Assert.True(Catalog.All.Count >= 50, $"only {Catalog.All.Count} exercises");
        Assert.Equal(Catalog.All.Count, Catalog.All.Select(e => e.Id).Distinct().Count());
        Assert.All(Catalog.All, e => Assert.Same(e, Catalog.Find(e.Id)));
    }

    [Fact]
    public void Every_exercise_is_complete()
    {
        Assert.All(Catalog.All, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.False(string.IsNullOrWhiteSpace(e.Prompt));
            Assert.Contains(e.Difficulty, new[] { "easy", "medium", "hard" });
        });
    }

    [Fact]
    public void Every_answer_and_check_query_passes_the_guard()
    {
        foreach (var e in Catalog.All)
        foreach (var db in Enum.GetValues<Db>())
        {
            var answer = e.AnswerFor(db);
            Assert.False(string.IsNullOrWhiteSpace(answer), $"#{e.Id} has no {db} answer");
            Assert.True(SqlGuard.Validate(db, answer) is null, $"#{e.Id} {db} answer: {SqlGuard.Validate(db, answer)}");
            if (e.CheckFor(db) is { } check)
                Assert.True(SqlGuard.Validate(db, check) is null, $"#{e.Id} {db} check: {SqlGuard.Validate(db, check)}");
        }
    }

    [Fact]
    public void Write_exercises_have_a_check_query()
    {
        var writes = Catalog.All.Where(e => e.Category is "INSERT" or "UPDATE" or "DELETE").ToList();
        Assert.NotEmpty(writes);
        Assert.All(writes, e => Assert.NotNull(e.Check));
    }
}
