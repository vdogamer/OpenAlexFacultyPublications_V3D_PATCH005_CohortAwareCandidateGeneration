using System.Text;

namespace OpenAlex.Reporting;

public sealed class CsvExportService
{
    public string CreateAcceptedPublicationsCsv(IEnumerable<AcceptedPublicationExportRow> rows)
    {
        var sb = new StringBuilder();

        sb.AppendLine(
            "FacultyDisplayName,PrimaryEmail,DepartmentName,DivisionName,AppointmentTitle," +
            "AuthorDisplayName,AuthorOpenAlexId,AuthorOrcid,WorkTitle,PublicationYear,PublicationDate," +
            "WorkDoi,DoiUrl,WorkOpenAlexId,WorkType,CitedByCount,DecisionStatus,ReviewedBy,ReviewedAtUtc");

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(row.FacultyDisplayName),
                Csv(row.PrimaryEmail),
                Csv(row.DepartmentName),
                Csv(row.DivisionName),
                Csv(row.AppointmentTitle),
                Csv(row.AuthorDisplayName),
                Csv(row.AuthorOpenAlexId),
                Csv(row.AuthorOrcid),
                Csv(row.WorkTitle),
                Csv(row.PublicationYear?.ToString()),
                Csv(row.PublicationDate?.ToString("yyyy-MM-dd")),
                Csv(row.WorkDoi),
                Csv(row.DoiUrl),
                Csv(row.WorkOpenAlexId),
                Csv(row.WorkType),
                Csv(row.CitedByCount?.ToString()),
                Csv(row.DecisionStatus),
                Csv(row.ReviewedBy),
                Csv(row.ReviewedAtUtc?.ToString("u"))
            }));
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;

        // Escape embedded double-quotes by doubling them, then wrap the whole value in quotes.
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
