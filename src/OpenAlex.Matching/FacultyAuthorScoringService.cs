namespace OpenAlex.Matching;

public sealed class FacultyAuthorScoringService
{
    public MatchScoreResult Score(FacultyAuthorMatchInput input)
    {
        var result = new MatchScoreResult();

        if (!string.IsNullOrWhiteSpace(input.FacultyOrcid) &&
            string.Equals(NormalizeOrcid(input.FacultyOrcid), NormalizeOrcid(input.AuthorOrcid), StringComparison.OrdinalIgnoreCase))
        {
            result.Score += 100;
            result.Evidence.Add("ORCID exact match");
            result.CandidateStatus = "StrongCandidate";
        }

        if (!string.IsNullOrWhiteSpace(input.ClaimedOpenAlexAuthorId) &&
            string.Equals(input.ClaimedOpenAlexAuthorId, input.AuthorOpenAlexId, StringComparison.OrdinalIgnoreCase))
        {
            result.Score += 100;
            result.Evidence.Add("Claimed OpenAlex Author ID exact match");
            result.CandidateStatus = "StrongCandidate";
        }

        if (NamesLooselyMatch(input.FacultyDisplayName, input.AuthorDisplayName))
        {
            result.Score += 40;
            result.Evidence.Add("Display names loosely match");
        }

        if (!string.IsNullOrWhiteSpace(input.LastKnownInstitutionName))
        {
            result.Score += 10;
            result.Evidence.Add("OpenAlex author has institution evidence");
        }

        if (result.Score >= 90)
        {
            result.CandidateStatus = "StrongCandidate";
        }
        else if (result.Score >= 40)
        {
            result.CandidateStatus = "NeedsReview";
        }
        else
        {
            result.CandidateStatus = "WeakCandidate";
        }

        return result;
    }

    private static string? NormalizeOrcid(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Replace("https://orcid.org/", "", StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static bool NamesLooselyMatch(string left, string right)
    {
        var l = NormalizeName(left);
        var r = NormalizeName(right);

        if (l == r)
        {
            return true;
        }

        var lParts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rParts = r.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return lParts.Length >= 2 &&
               rParts.Length >= 2 &&
               lParts[^1] == rParts[^1] &&
               lParts[0][0] == rParts[0][0];
    }

    private static string NormalizeName(string value)
    {
        return new string(value
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            .ToArray())
            .Trim();
    }
}
