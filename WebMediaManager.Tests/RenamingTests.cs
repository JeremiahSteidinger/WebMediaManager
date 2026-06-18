using WebMediaManager.Core.Renaming;

namespace WebMediaManager.Tests;

public class TokenEngineTests
{
    private readonly TokenEngine _engine = new();

    private static Dictionary<string, string?> Tokens(params (string, string?)[] pairs)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }
        return d;
    }

    [Fact]
    public void Substitutes_tokens()
    {
        var result = _engine.Render("{title} ({year})", Tokens(("title", "Inception"), ("year", "2010")));
        Assert.Equal("Inception (2010)", result);
    }

    [Fact]
    public void Optional_group_kept_when_token_present()
    {
        var result = _engine.Render("{title} <[tmdb-{tmdbid}]>", Tokens(("title", "Inception"), ("tmdbid", "27205")));
        Assert.Equal("Inception [tmdb-27205]", result);
    }

    [Fact]
    public void Optional_group_dropped_when_token_empty()
    {
        var result = _engine.Render("{title} <[tmdb-{tmdbid}]>", Tokens(("title", "Inception"), ("tmdbid", null)));
        Assert.Equal("Inception", result);
    }

    [Fact]
    public void Unknown_token_becomes_empty()
    {
        var result = _engine.Render("{title}{nope}", Tokens(("title", "X")));
        Assert.Equal("X", result);
    }

    [Fact]
    public void Token_lookup_is_case_insensitive()
    {
        var result = _engine.Render("{Title}", Tokens(("title", "X")));
        Assert.Equal("X", result);
    }
}

public class FilenameSanitizerTests
{
    [Theory]
    [InlineData("Title: Subtitle", "Title - Subtitle")]
    [InlineData("a/b\\c", "a-b-c")]
    [InlineData("a<b>c\"d|e?f*g", "abcdefg")]
    [InlineData("trailing dot.", "trailing dot")]
    [InlineData("  spaced   out  ", "spaced out")]
    public void Sanitizes(string input, string expected) =>
        Assert.Equal(expected, FilenameSanitizer.Sanitize(input));
}

public class RenamePlannerTests
{
    private const string Root = "/media/movies";

    [Fact]
    public void Orders_files_before_their_parent_folder()
    {
        var plan = new RenamePlanner().BuildPlan(Root,
        [
            new PlannedMove("Inception (2010)", "Inception [tmdb-27205]", RenameMoveKind.Folder),
            new PlannedMove("Inception (2010)/old.mkv", "Inception (2010)/new.mkv", RenameMoveKind.File),
        ], new FakeFs());

        Assert.Equal(RenameMoveKind.File, plan.Ops[0].Kind);
        Assert.Equal(RenameMoveKind.Folder, plan.Ops[1].Kind);
        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public void Flags_two_moves_to_same_target()
    {
        var plan = new RenamePlanner().BuildPlan(Root,
        [
            new PlannedMove("a.mkv", "same.mkv", RenameMoveKind.File),
            new PlannedMove("b.mkv", "same.mkv", RenameMoveKind.File),
        ], new FakeFs());

        Assert.True(plan.HasConflicts);
        Assert.Single(plan.Ops, o => o.Conflict == "Two items map to the same name.");
    }

    [Fact]
    public void Flags_target_escaping_root()
    {
        var plan = new RenamePlanner().BuildPlan(Root,
            [new PlannedMove("a.mkv", "../escape.mkv", RenameMoveKind.File)], new FakeFs());

        Assert.True(plan.HasConflicts);
        Assert.Contains(plan.Ops, o => o.Conflict!.Contains("escapes"));
    }

    [Fact]
    public void Flags_existing_target_that_is_not_a_source()
    {
        var fs = new FakeFs();
        fs.Files.Add(Abs("taken.mkv"));
        var plan = new RenamePlanner().BuildPlan(Root,
            [new PlannedMove("a.mkv", "taken.mkv", RenameMoveKind.File)], fs);

        Assert.True(plan.HasConflicts);
        Assert.Contains(plan.Ops, o => o.Conflict == "Target already exists.");
    }

    [Fact]
    public void Allows_target_that_is_itself_a_source()
    {
        // a -> b while b -> c: b exists but is being moved away, so not a conflict.
        var fs = new FakeFs();
        fs.Files.Add(Abs("a.mkv"));
        fs.Files.Add(Abs("b.mkv"));
        var plan = new RenamePlanner().BuildPlan(Root,
        [
            new PlannedMove("a.mkv", "b.mkv", RenameMoveKind.File),
            new PlannedMove("b.mkv", "c.mkv", RenameMoveKind.File),
        ], fs);

        Assert.False(plan.HasConflicts);
    }

    private static string Abs(string relative) =>
        Path.GetFullPath(Path.Combine(Path.GetFullPath(Root), relative));

    [Fact]
    public void Drops_no_op_moves()
    {
        var plan = new RenamePlanner().BuildPlan(Root,
            [new PlannedMove("same.mkv", "same.mkv", RenameMoveKind.File)], new FakeFs());

        Assert.True(plan.IsEmpty);
    }

    private sealed class FakeFs : IFileSystem
    {
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string absolutePath) => Files.Contains(absolutePath);
        public bool DirectoryExists(string absolutePath) => Dirs.Contains(absolutePath);
        public void MoveFile(string fromAbsolute, string toAbsolute) { }
        public void MoveDirectory(string fromAbsolute, string toAbsolute) { }
    }
}
