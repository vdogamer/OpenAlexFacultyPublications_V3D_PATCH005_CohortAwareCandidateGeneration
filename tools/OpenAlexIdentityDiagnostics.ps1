<#
V3E manual identity diagnostics for OpenAlex faculty disambiguation.

Purpose:
  Capture reproducible evidence for known identity-resolution edge cases before changing the matching algorithm.

Default test cases:
  - Susan M. Pollart: author-profile fragmentation / multiple plausible OpenAlex author IDs.
  - Jeffrey Martens: award discovery via name search when ORCID search is incomplete or empty.

Outputs:
  artifacts/openalex-identity-diagnostics/<timestamp>/
    author-search-*.json
    author-candidates.csv
    author-work-samples.csv
    award-search-*.json
    award-search-*-error.json, if an individual award query fails
    award-candidates.csv

Notes:
  - This script does not write to SQL Server.
  - This script does not require an OpenAlex API key, but using one is recommended.
  - PowerShell 5.1-compatible syntax is intentional for older Windows environments.
#>

param(
    [string]$ApiKey = $env:OPENALEX_API_KEY,
    [string]$OutputRoot = "artifacts/openalex-identity-diagnostics",
    [int]$PerPage = 25,
    [int]$WorkSamplePerAuthor = 10,
    [string]$JeffreyMartensOrcid = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function New-SafeFileToken {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "empty"
    }

    return ($Value.ToLowerInvariant() -replace '[^a-z0-9]+', '-') -replace '(^-+|-+$)', ''
}

function New-OpenAlexUri {
    param(
        [string]$Path,
        [hashtable]$Query
    )

    $builder = New-Object System.UriBuilder("https://api.openalex.org$Path")
    $pairs = New-Object System.Collections.Generic.List[string]

    foreach ($key in $Query.Keys) {
        $value = $Query[$key]
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            $encodedKey = [System.Uri]::EscapeDataString([string]$key)
            $encodedValue = [System.Uri]::EscapeDataString([string]$value)
            $pairs.Add("$encodedKey=$encodedValue")
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
        $pairs.Add("api_key=$([System.Uri]::EscapeDataString($ApiKey))")
    }

    $builder.Query = [string]::Join("&", $pairs.ToArray())
    return $builder.Uri.AbsoluteUri
}

function Get-WebExceptionResponseBody {
    param($Exception)

    if ($null -eq $Exception -or $null -eq $Exception.Response) {
        return ""
    }

    try {
        $stream = $Exception.Response.GetResponseStream()
        if ($null -eq $stream) {
            return ""
        }

        $reader = New-Object System.IO.StreamReader($stream)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Close()
        }
    }
    catch {
        return ""
    }
}

function Invoke-OpenAlexGet {
    param(
        [string]$Path,
        [hashtable]$Query,
        [string]$RawOutputPath,
        [switch]$ContinueOnError
    )

    $uri = New-OpenAlexUri -Path $Path -Query $Query
    Write-Host "GET $uri"

    try {
        $response = Invoke-RestMethod -Uri $uri -Method Get -Headers @{ "User-Agent" = "OpenAlexFacultyPublications-V3E-Diagnostics" }

        if ($RawOutputPath) {
            $response | ConvertTo-Json -Depth 100 | Out-File -FilePath $RawOutputPath -Encoding UTF8
        }

        return $response
    }
    catch {
        $responseBody = Get-WebExceptionResponseBody -Exception $_.Exception
        $errorPath = $null

        if ($RawOutputPath) {
            $errorPath = $RawOutputPath -replace '\.json$', '-error.json'
            [pscustomobject]@{
                Uri = $uri
                ErrorMessage = $_.Exception.Message
                ResponseBody = $responseBody
            } | ConvertTo-Json -Depth 20 | Out-File -FilePath $errorPath -Encoding UTF8
        }

        Write-Warning "OpenAlex request failed. Error was saved to: $errorPath"
        Write-Warning $_.Exception.Message

        if ($ContinueOnError) {
            return $null
        }

        throw
    }
}

function Get-ShortOpenAlexId {
    param([string]$Id)

    if ([string]::IsNullOrWhiteSpace($Id)) {
        return $null
    }

    $trimmed = $Id.Trim().TrimEnd('/')
    $lastSlash = $trimmed.LastIndexOf('/')
    if ($lastSlash -ge 0 -and $lastSlash -lt ($trimmed.Length - 1)) {
        return $trimmed.Substring($lastSlash + 1)
    }

    return $trimmed
}

function Join-Names {
    param($Items)

    if ($null -eq $Items) {
        return ""
    }

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($item in $Items) {
        if ($null -ne $item -and $null -ne $item.display_name) {
            $names.Add([string]$item.display_name)
        }
    }

    return [string]::Join(" | ", $names.ToArray())
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = Join-Path $OutputRoot $timestamp
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Writing diagnostics to: $outputDir"

$authorSearches = @(
    @{ Label = "susan-m-pollart-search"; Search = "Susan M. Pollart" },
    @{ Label = "susan-pollart-search"; Search = "Susan Pollart" },
    @{ Label = "s-m-pollart-search"; Search = "S M Pollart" }
)

$authorRows = New-Object System.Collections.Generic.List[object]
$seenAuthorIds = @{}

foreach ($searchSpec in $authorSearches) {
    $label = $searchSpec.Label
    $search = $searchSpec.Search
    $rawPath = Join-Path $outputDir ("author-search-{0}.json" -f $label)

    $response = Invoke-OpenAlexGet -Path "/authors" -Query @{
        search = $search
        "per-page" = $PerPage
        select = "id,display_name,orcid,works_count,cited_by_count,last_known_institutions,affiliations,updated_date"
    } -RawOutputPath $rawPath

    foreach ($author in $response.results) {
        $shortId = Get-ShortOpenAlexId -Id $author.id
        if (-not $seenAuthorIds.ContainsKey($shortId)) {
            $seenAuthorIds[$shortId] = $true
        }

        $authorRows.Add([pscustomobject]@{
            FacultyTestCase = "Susan M. Pollart"
            DiscoveryMethod = $label
            SearchTerm = $search
            AuthorOpenAlexId = $author.id
            AuthorOpenAlexShortId = $shortId
            DisplayName = $author.display_name
            Orcid = $author.orcid
            WorksCount = $author.works_count
            CitedByCount = $author.cited_by_count
            LastKnownInstitutions = Join-Names -Items $author.last_known_institutions
            UpdatedDate = $author.updated_date
        })
    }
}

$authorRows |
    Sort-Object FacultyTestCase, AuthorOpenAlexShortId, DiscoveryMethod |
    Export-Csv -Path (Join-Path $outputDir "author-candidates.csv") -NoTypeInformation -Encoding UTF8

$workRows = New-Object System.Collections.Generic.List[object]

foreach ($authorShortId in $seenAuthorIds.Keys) {
    $rawPath = Join-Path $outputDir ("works-by-author-{0}.json" -f (New-SafeFileToken -Value $authorShortId))

    $response = Invoke-OpenAlexGet -Path "/works" -Query @{
        filter = "authorships.author.id:$authorShortId"
        sort = "publication_date:desc"
        "per-page" = $WorkSamplePerAuthor
        select = "id,display_name,title,doi,publication_year,publication_date,type,authorships,locations,primary_location"
    } -RawOutputPath $rawPath

    foreach ($work in $response.results) {
        $matchedInstitutions = New-Object System.Collections.Generic.List[string]
        foreach ($authorship in $work.authorships) {
            if ($null -ne $authorship.author -and (Get-ShortOpenAlexId -Id $authorship.author.id) -eq $authorShortId) {
                foreach ($institution in $authorship.institutions) {
                    if ($null -ne $institution.display_name) {
                        $matchedInstitutions.Add([string]$institution.display_name)
                    }
                }
            }
        }

        $workRows.Add([pscustomobject]@{
            FacultyTestCase = "Susan M. Pollart"
            AuthorOpenAlexShortId = $authorShortId
            WorkOpenAlexId = $work.id
            WorkTitle = $(if ($work.title) { $work.title } else { $work.display_name })
            Doi = $work.doi
            PublicationYear = $work.publication_year
            PublicationDate = $work.publication_date
            WorkType = $work.type
            MatchedAuthorInstitutions = [string]::Join(" | ", $matchedInstitutions.ToArray())
        })
    }
}

$workRows |
    Sort-Object AuthorOpenAlexShortId, PublicationYear -Descending |
    Export-Csv -Path (Join-Path $outputDir "author-work-samples.csv") -NoTypeInformation -Encoding UTF8

$awardSelect = "id,display_name,funder_award_id,funder,amount,currency,start_year,end_year,start_date,end_date,lead_investigator,co_lead_investigator,investigators,funded_outputs_count,doi,provenance"

$awardSearches = New-Object System.Collections.Generic.List[hashtable]
$awardSearches.Add(@{ Label = "jeffrey-martens-search"; Query = @{ search = "Jeffrey Martens"; "per-page" = $PerPage; select = $awardSelect } })
$awardSearches.Add(@{ Label = "jeffrey-martens-name-filter"; Query = @{ filter = "lead_investigator.given_name:Jeffrey,lead_investigator.family_name:Martens"; "per-page" = $PerPage; select = $awardSelect } })
$awardSearches.Add(@{ Label = "jeffrey-martens-text-search-filter"; Query = @{ filter = "text.search:Jeffrey Martens"; "per-page" = $PerPage; select = $awardSelect } })

if (-not [string]::IsNullOrWhiteSpace($JeffreyMartensOrcid)) {
    $awardSearches.Add(@{ Label = "jeffrey-martens-orcid-filter"; Query = @{ filter = "lead_investigator.orcid:$JeffreyMartensOrcid"; "per-page" = $PerPage; select = $awardSelect } })
}

$awardRows = New-Object System.Collections.Generic.List[object]
$awardQueryErrors = New-Object System.Collections.Generic.List[object]

foreach ($searchSpec in $awardSearches) {
    $label = $searchSpec.Label
    $rawPath = Join-Path $outputDir ("award-search-{0}.json" -f $label)
    $response = Invoke-OpenAlexGet -Path "/awards" -Query $searchSpec.Query -RawOutputPath $rawPath -ContinueOnError

    if ($null -eq $response) {
        $awardQueryErrors.Add([pscustomobject]@{
            Label = $label
            RawOutputPath = $rawPath
            ErrorOutputPath = ($rawPath -replace '\.json$', '-error.json')
        })
        continue
    }

    foreach ($award in $response.results) {
        $lead = $award.lead_investigator
        $leadName = ""
        $leadOrcid = ""
        if ($null -ne $lead) {
            if ($lead.display_name) {
                $leadName = $lead.display_name
            } elseif ($lead.given_name -or $lead.family_name) {
                $leadName = ((@($lead.given_name, $lead.family_name) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join " ")
            }
            if ($lead.orcid) {
                $leadOrcid = $lead.orcid
            }
        }

        $funderName = ""
        if ($null -ne $award.funder -and $award.funder.display_name) {
            $funderName = $award.funder.display_name
        }

        $dedupeFingerprint = [string]::Join("|", @(
            $funderName,
            $award.funder_award_id,
            $award.display_name,
            $award.start_year,
            $award.end_year,
            $award.amount,
            $award.currency
        ))

        $awardRows.Add([pscustomobject]@{
            FacultyTestCase = "Jeffrey Martens"
            DiscoveryMethod = $label
            AwardOpenAlexId = $award.id
            DisplayName = $award.display_name
            FunderAwardId = $award.funder_award_id
            FunderName = $funderName
            Amount = $award.amount
            Currency = $award.currency
            StartYear = $award.start_year
            EndYear = $award.end_year
            StartDate = $award.start_date
            EndDate = $award.end_date
            LeadInvestigatorName = $leadName
            LeadInvestigatorOrcid = $leadOrcid
            FundedOutputsCount = $award.funded_outputs_count
            Doi = $award.doi
            Provenance = $award.provenance
            DedupeFingerprint = $dedupeFingerprint
        })
    }
}

$awardRows |
    Sort-Object FacultyTestCase, FunderName, FunderAwardId, StartYear, DisplayName |
    Export-Csv -Path (Join-Path $outputDir "award-candidates.csv") -NoTypeInformation -Encoding UTF8

if ($awardQueryErrors.Count -gt 0) {
    $awardQueryErrors |
        Export-Csv -Path (Join-Path $outputDir "award-query-errors.csv") -NoTypeInformation -Encoding UTF8
}

$summary = [pscustomobject]@{
    GeneratedAt = (Get-Date).ToString("s")
    OutputDirectory = $outputDir
    SusanPollartUniqueAuthorCandidateCount = $seenAuthorIds.Count
    SusanPollartWorkSampleRows = $workRows.Count
    JeffreyMartensAwardRows = $awardRows.Count
    AwardQueryErrorCount = $awardQueryErrors.Count
    ApiKeyUsed = (-not [string]::IsNullOrWhiteSpace($ApiKey))
}

$summary | ConvertTo-Json -Depth 10 | Out-File -FilePath (Join-Path $outputDir "diagnostic-summary.json") -Encoding UTF8
$summary | Format-List

Write-Host "Done. Review these files first:"
Write-Host "  $(Join-Path $outputDir 'author-candidates.csv')"
Write-Host "  $(Join-Path $outputDir 'author-work-samples.csv')"
Write-Host "  $(Join-Path $outputDir 'award-candidates.csv')"
if ($awardQueryErrors.Count -gt 0) {
    Write-Warning "One or more award queries failed. Review: $(Join-Path $outputDir 'award-query-errors.csv')"
}
