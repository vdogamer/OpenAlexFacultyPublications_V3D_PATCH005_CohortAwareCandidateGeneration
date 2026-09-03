using System.Text.Json;

namespace OpenAlex.OpenAlexClient;

public static class AcademicOnlineDepartmentParser
{
    public static string? GetPreferredDepartmentName(JsonElement root)
    {
        var affiliations = GetAffiliations(root);
        if (affiliations.Count == 0)
        {
            return null;
        }

        var primary = affiliations.FirstOrDefault(item => item.IsPrimary);
        if (primary is not null)
        {
            return primary.Name;
        }

        // Prefer a leaf organizational unit. If one record's ID is used as another
        // record's parent_id, that record is a parent rather than the most specific unit.
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
        return GetAffiliations(root)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DepartmentAffiliation> GetAffiliations(JsonElement root)
    {
        foreach (var propertyName in new[]
                 {
                     "department",
                     "departments",
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

        return new List<DepartmentAffiliation>();
    }

    private static List<DepartmentAffiliation> ParseValue(JsonElement value)
    {
        var affiliations = new List<DepartmentAffiliation>();

        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                affiliations.Add(new DepartmentAffiliation
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
                    affiliations.Add(new DepartmentAffiliation
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
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static DepartmentAffiliation? ParseObject(JsonElement item)
    {
        var name =
            GetString(item, "name")
            ?? GetString(item, "display_name")
            ?? GetString(item, "department_name");

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new DepartmentAffiliation
        {
            Id = GetString(item, "id"),
            Name = name.Trim(),
            ParentId = GetString(item, "parent_id"),
            ParentName = GetString(item, "parent_name"),
            IsPrimary = GetBool(item, "is_primary")
        };
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

        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private sealed class DepartmentAffiliation
    {
        public string? Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? ParentId { get; init; }
        public string? ParentName { get; init; }
        public bool IsPrimary { get; init; }
    }
}
