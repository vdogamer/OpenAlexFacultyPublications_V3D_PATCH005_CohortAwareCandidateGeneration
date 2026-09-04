using System.Text.Json;

namespace OpenAlex.OpenAlexClient;

public static class AcademicOnlineDepartmentParser
{
    public static string? GetPreferredDepartmentName(JsonElement root)
    {
        var affiliations = GetDepartmentAffiliations(root);
        if (affiliations.Count == 0)
        {
            return null;
        }

        var primary = affiliations.FirstOrDefault(item => item.IsPrimary);
        if (primary is not null)
        {
            return primary.Name;
        }

        var parentIds = affiliations
            .Where(item => !string.IsNullOrWhiteSpace(item.ParentId))
            .Select(item => item.ParentId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var leaves = affiliations
            .Where(item =>
                string.IsNullOrWhiteSpace(item.Id) ||
                !parentIds.Contains(item.Id))
            .ToList();

        var preferredLeaf = leaves
            .OrderByDescending(item =>
                item.Name.StartsWith(
                    "Department of ",
                    StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item =>
                !string.IsNullOrWhiteSpace(item.ParentId))
            .FirstOrDefault();

        return preferredLeaf?.Name ?? affiliations[^1].Name;
    }

    public static IReadOnlyList<string> GetDepartmentNames(JsonElement root)
    {
        return GetDepartmentAffiliations(root)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<AcademicOnlineDepartmentAffiliation>
        GetDepartmentAffiliations(JsonElement root)
    {
        foreach (var propertyName in new[]
                 {
                     "departments",
                     "department",
                     "department_name"
                 })
        {
            if (!root.TryGetProperty(propertyName, out var value) ||
                value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var affiliations = ParseValue(value);
            if (affiliations.Count > 0)
            {
                return affiliations;
            }
        }

        return Array.Empty<AcademicOnlineDepartmentAffiliation>();
    }

    private static List<AcademicOnlineDepartmentAffiliation>
        ParseValue(JsonElement value)
    {
        var affiliations =
            new List<AcademicOnlineDepartmentAffiliation>();

        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                affiliations.Add(
                    new AcademicOnlineDepartmentAffiliation
                    {
                        Name = name.Trim()
                    });
            }

            return affiliations;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var affiliation = ParseObject(value);
            if (affiliation is not null)
            {
                affiliations.Add(affiliation);
            }

            return affiliations;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return affiliations;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    affiliations.Add(
                        new AcademicOnlineDepartmentAffiliation
                        {
                            Name = name.Trim()
                        });
                }

                continue;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                var affiliation = ParseObject(item);
                if (affiliation is not null)
                {
                    affiliations.Add(affiliation);
                }
            }
        }

        return affiliations
            .GroupBy(
                BuildAffiliationKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static AcademicOnlineDepartmentAffiliation?
        ParseObject(JsonElement item)
    {
        var name =
            GetString(item, "name")
            ?? GetString(item, "display_name")
            ?? GetString(item, "department_name");

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new AcademicOnlineDepartmentAffiliation
        {
            Id = GetString(item, "id"),
            Name = name.Trim(),
            Slug = GetString(item, "slug"),
            ParentId = GetString(item, "parent_id"),
            ParentName = GetString(item, "parent_name"),
            ParentSlug = GetString(item, "parent_slug"),
            IsPrimary = GetBool(item, "is_primary")
        };
    }

    private static string BuildAffiliationKey(
        AcademicOnlineDepartmentAffiliation affiliation)
    {
        if (!string.IsNullOrWhiteSpace(affiliation.Id))
        {
            return "id:" + affiliation.Id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(affiliation.Slug))
        {
            return "slug:" + affiliation.Slug.Trim();
        }

        return "name:" + affiliation.Name.Trim();
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static bool GetBool(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number))
        {
            return number != 0;
        }

        return bool.TryParse(
            value.ToString(),
            out var parsed) &&
            parsed;
    }
}
