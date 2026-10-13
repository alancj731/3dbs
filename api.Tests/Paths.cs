namespace Api.Tests;

static class Paths
{
    /// <summary>The api/ project folder: its content root, where Practice/Exercises.json lives.</summary>
    public static string Api { get; } = FindApi();

    static string FindApi()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var api = Path.Combine(dir.FullName, "api");
            if (File.Exists(Path.Combine(api, "Api.csproj"))) return api;
        }
        throw new InvalidOperationException("Could not find the api/ folder above " + AppContext.BaseDirectory);
    }
}
