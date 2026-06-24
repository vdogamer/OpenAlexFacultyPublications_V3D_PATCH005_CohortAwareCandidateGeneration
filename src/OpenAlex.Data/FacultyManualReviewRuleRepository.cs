using Microsoft.Data.SqlClient;
using OpenAlex.Data.Models;

namespace OpenAlex.Data;

public sealed class FacultyManualReviewRuleRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public FacultyManualReviewRuleRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyDictionary<long, FacultyManualReviewRule>> LoadActiveRulesAsync(
        IReadOnlyCollection<long> facultyIds,
        CancellationToken cancellationToken = default)
    {
        if (facultyIds.Count == 0)
        {
            return new Dictionary<long, FacultyManualReviewRule>();
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await ObjectExistsAsync(connection, "openalex.FacultyIdentityReviewConstraint", "U", cancellationToken))
        {
            return new Dictionary<long, FacultyManualReviewRule>();
        }

        var facultyFilter = string.Join(",", facultyIds.Distinct().OrderBy(id => id).Select(id => id.ToString()));

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT
    FacultyId,
    RequiresVerifiedIdentifierForLikelyIdentity,
    RequiresManualReviewForInitialOnlyName,
    RequiresAuthorSpecificAffiliationEvidence,
    RequiresKnownCoauthorOrTopicEvidence,
    ConstraintReason
FROM openalex.FacultyIdentityReviewConstraint
WHERE IsActive = 1
  AND FacultyId IN ({facultyFilter});";

        var rules = new Dictionary<long, FacultyManualReviewRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var facultyId = reader.GetInt64(reader.GetOrdinal("FacultyId"));
            rules[facultyId] = new FacultyManualReviewRule
            {
                FacultyId = facultyId,
                RequireVerifiedSeedForLikelyStatus = reader.GetBoolean(reader.GetOrdinal("RequiresVerifiedIdentifierForLikelyIdentity")),
                UseManualReviewForAbbreviatedSearch = reader.GetBoolean(reader.GetOrdinal("RequiresManualReviewForInitialOnlyName")),
                RequireAuthorAffiliationSupport = reader.GetBoolean(reader.GetOrdinal("RequiresAuthorSpecificAffiliationEvidence")),
                RequireCollaborationOrTopicSupport = reader.GetBoolean(reader.GetOrdinal("RequiresKnownCoauthorOrTopicEvidence")),
                Reason = GetString(reader, "ConstraintReason")
            };
        }

        return rules;
    }

    private static string? GetString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string objectName, string objectType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@ObjectName", objectName);
        command.Parameters.AddWithValue("@ObjectType", objectType);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
