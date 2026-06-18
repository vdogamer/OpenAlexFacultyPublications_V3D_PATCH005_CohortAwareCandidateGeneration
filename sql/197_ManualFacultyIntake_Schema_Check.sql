/*
V3D PATCH003 — Manual Faculty Intake Schema Check
*/

SET NOCOUNT ON;

SELECT DB_NAME() AS CurrentDatabase;

IF OBJECT_ID(N'openalex.Faculty', N'U') IS NULL
BEGIN
    RAISERROR('openalex.Faculty does not exist.', 16, 1);
    RETURN;
END;

SELECT
    c.column_id,
    c.name AS ColumnName,
    t.name AS DataType,
    c.max_length,
    c.is_nullable
FROM sys.columns c
JOIN sys.types t
    ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(N'openalex.Faculty')
ORDER BY c.column_id;

SELECT TOP 20 *
FROM openalex.Faculty
ORDER BY FacultyId;
