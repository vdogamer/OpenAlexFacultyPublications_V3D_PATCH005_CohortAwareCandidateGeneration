namespace OpenAlex.Tests;

public sealed class NameScoringTests
{
    [Theory]
    [InlineData("Susan Pollart", "Susan Pollart", 100)]
    [InlineData("Susan Pollart", "S Pollart", 80)]
    [InlineData("Susan Pollart", "Sue Pollart", 55)]
    [InlineData("Susan Pollart", "John Smith", 0)]
    public void ScoreName_ReturnsExpectedBaselineScore(string facultyName, string authorName, int minimumExpectedScore)
    {
        var score = TestNameScorer.ScoreName(facultyName, authorName);
        Assert.True(score >= minimumExpectedScore, $"Expected score >= {minimumExpectedScore}, but got {score}.");
    }
}

internal static class TestNameScorer
{
    public static decimal ScoreName(string? facultyDisplayName, string? authorDisplayName)
    {
        if (string.IsNullOrWhiteSpace(facultyDisplayName) || string.IsNullOrWhiteSpace(authorDisplayName)) return 0m;
        var facultyParsed = ParseName(facultyDisplayName);
        var authorParsed = ParseName(authorDisplayName);
        var facultyFull = NormalizeName(facultyDisplayName);
        var authorFull = NormalizeName(authorDisplayName);
        if (facultyFull == authorFull) return 100m;
        if (facultyParsed.Last == authorParsed.Last && facultyParsed.First == authorParsed.First && !string.IsNullOrWhiteSpace(facultyParsed.First)) return 95m;
        if (facultyParsed.Last == authorParsed.Last && FirstInitialMatches(facultyParsed.First, authorParsed.First)) return 80m;
        if (facultyParsed.Last == authorParsed.Last && !string.IsNullOrWhiteSpace(facultyParsed.First) && authorFull.Contains(facultyParsed.First)) return 75m;
        if (facultyParsed.Last == authorParsed.Last) return 55m;
        return 0m;
    }

    private static (string? First, string? Last) ParseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1) return (NormalizeName(tokens[0]), null);
        return (NormalizeName(tokens[0]), NormalizeName(tokens[^1]));
    }

    private static bool FirstInitialMatches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && char.ToUpperInvariant(left[0]) == char.ToUpperInvariant(right[0]);

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray()).Trim();
    }
}
