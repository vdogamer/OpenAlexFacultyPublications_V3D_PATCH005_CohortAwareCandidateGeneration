/*
V3E PATCH021D — Unresolved Department Diagnostic Log

Run after a PATCH021D discovery request completes.
*/

SET NOCOUNT ON;

SELECT TOP (20)
    CreatedAtUtc,
    LogLevel,
    Category,
    EventName,
    Message
FROM openalex.OperationalLog
WHERE Category = N'AcademicOnlineDepartmentDiscovery'
  AND EventName = N'DepartmentUnresolvedDiagnostic'
ORDER BY CreatedAtUtc DESC;
