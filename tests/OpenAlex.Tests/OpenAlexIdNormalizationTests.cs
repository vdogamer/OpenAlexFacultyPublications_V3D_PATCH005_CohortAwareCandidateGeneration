namespace OpenAlex.Tests;

public sealed class OpenAlexIdNormalizationTests
{
    [Theory]
    [InlineData("I4210107925", "https://openalex.org/I4210107925")]
    [InlineData("https://openalex.org/I4210107925", "https://openalex.org/I4210107925")]
    [InlineData("https://openalex.org/I4210107925/", "https://openalex.org/I4210107925")]
    [InlineData(" i4210107925 ", "https://openalex.org/I4210107925")]
    public void NormalizeInstitutionId_ReturnsExpectedFullId(string input, string expected)
    {
        Assert.Equal(expected, TestOpenAlexIdNormalizer.NormalizeOpenAlexId(input));
    }

    [Theory]
    [InlineData("https://openalex.org/A123", "A123")]
    [InlineData("A123", "A123")]
    [InlineData(" https://openalex.org/W999/ ", "W999")]
    public void GetShortId_ReturnsExpectedShortId(string input, string expected)
    {
        Assert.Equal(expected, TestOpenAlexIdNormalizer.GetShortId(input));
    }
}

internal static class TestOpenAlexIdNormalizer
{
    public static string? NormalizeOpenAlexId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim().TrimEnd('/');
        if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return trimmed;
        if (trimmed.Length > 1 && char.IsLetter(trimmed[0])) return "https://openalex.org/" + trimmed.ToUpperInvariant();
        return trimmed;
    }

    public static string? GetShortId(string? value)
    {
        var normalized = NormalizeOpenAlexId(value);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < normalized.Length - 1 ? normalized[(slashIndex + 1)..] : normalized;
    }
}
