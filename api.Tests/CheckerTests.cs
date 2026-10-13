using Api.Practice;

namespace Api.Tests;

public class CheckerTests
{
    static QueryResult Result(string[] columns, params object?[][] rows) => new(columns, rows.ToList(), 0, false, 0);
    static QueryResult One(params object?[] values) => Result(["x"], values.Select(v => new[] { v }).ToArray());

    [Fact]
    public void Same_rows_in_any_order_are_correct_when_order_does_not_matter()
    {
        var (correct, message, missing, extra) = Checker.Compare(One(1, 2, 3), One(3, 1, 2), ordered: false, isWrite: false);
        Assert.True(correct);
        Assert.Equal("Correct!", message);
        Assert.Empty(missing);
        Assert.Empty(extra);
    }

    [Fact]
    public void Column_names_are_ignored()
    {
        var yours = Result(["a", "b"], [1, "x"]);
        var expected = Result(["Id", "Name"], [1, "x"]);
        Assert.True(Checker.Compare(yours, expected, ordered: false, isWrite: false).Correct);
    }

    [Fact]
    public void Wrong_order_fails_when_order_matters()
    {
        var r = Checker.Compare(One(2, 1), One(1, 2), ordered: true, isWrite: false);
        Assert.False(r.Correct);
        Assert.Contains("order is wrong", r.Message);
        Assert.True(Checker.Compare(One(1, 2), One(1, 2), ordered: true, isWrite: false).Correct);
    }

    [Fact]
    public void Column_count_mismatch_fails()
    {
        var r = Checker.Compare(Result(["a"], [1]), Result(["a", "b"], [1, 2]), ordered: false, isWrite: false);
        Assert.False(r.Correct);
        Assert.Equal("Your result has 1 column(s); expected 2.", r.Message);
    }

    [Fact]
    public void Write_exercises_describe_the_data_after_the_statement()
    {
        var r = Checker.Compare(One(1), One(1, 2), ordered: false, isWrite: true);
        Assert.Equal("After your statement the data has 1 row(s); expected 2.", r.Message);
    }

    [Fact]
    public void Duplicates_are_counted()
    {
        var r = Checker.Compare(One(1, 1, 2), One(1, 2, 2), ordered: false, isWrite: false);
        Assert.False(r.Correct);
        Assert.Contains("right number of rows (3)", r.Message);
        Assert.Equal(2, Assert.Single(r.Missing)[0]);
        Assert.Equal(1, Assert.Single(r.Extra)[0]);
    }

    [Fact]
    public void Lists_missing_and_extra_rows_up_to_twenty()
    {
        var r = Checker.Compare(One(Enumerable.Range(100, 30).Cast<object?>().ToArray()),
            One(Enumerable.Range(0, 30).Cast<object?>().ToArray()), ordered: false, isWrite: false);
        Assert.False(r.Correct);
        Assert.Equal(20, r.Missing.Count);
        Assert.Equal(20, r.Extra.Count);
    }

    [Theory]
    [InlineData(10, 10.00)]          // int vs decimal
    [InlineData(2.5, 2.499)]         // rounded to 2 decimals
    [InlineData(true, 1)]            // bool vs bit/tinyint
    [InlineData("abc   ", "abc")]    // CHAR padding
    [InlineData(null, null)]
    public void Values_from_different_drivers_compare_equal(object? a, object? b)
    {
        // Decimal literals can't be attributes; use decimal where SQL drivers would.
        static object? AsDriverValue(object? v) => v is double d ? (decimal)d : v;
        Assert.True(Checker.Compare(One(AsDriverValue(a)), One(b), ordered: false, isWrite: false).Correct);
    }

    [Fact]
    public void Null_is_not_an_empty_string()
    {
        Assert.False(Checker.Compare(One((object?)null), One(""), ordered: false, isWrite: false).Correct);
    }
}
