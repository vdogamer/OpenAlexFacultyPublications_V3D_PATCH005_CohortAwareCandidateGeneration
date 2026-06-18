/* OpenAlex Version 3C — Role Management Extensions */
SET NOCOUNT ON;
IF SCHEMA_ID(N'openalex') IS NULL EXEC(N'CREATE SCHEMA openalex');
GO
IF OBJECT_ID(N'openalex.AppRoleChangeAudit', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AppRoleChangeAudit
    (
        AppRoleChangeAuditId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppRoleChangeAudit PRIMARY KEY,
        TargetAppUserId BIGINT NULL,
        TargetUserName NVARCHAR(256) NULL,
        ChangedBy NVARCHAR(256) NOT NULL,
        ChangeType NVARCHAR(100) NOT NULL,
        RoleName NVARCHAR(100) NULL,
        OldValue NVARCHAR(MAX) NULL,
        NewValue NVARCHAR(MAX) NULL,
        ChangeReason NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AppRoleChangeAudit_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END
GO
IF OBJECT_ID(N'openalex.AppExternalGroupRoleMapping', N'U') IS NULL
BEGIN
    CREATE TABLE openalex.AppExternalGroupRoleMapping
    (
        AppExternalGroupRoleMappingId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppExternalGroupRoleMapping PRIMARY KEY,
        ExternalGroupValue NVARCHAR(512) NOT NULL,
        MatchType NVARCHAR(50) NOT NULL CONSTRAINT DF_AppExternalGroupRoleMapping_MatchType DEFAULT ('Exact'),
        AppRoleId BIGINT NOT NULL,
        RoleName NVARCHAR(100) NOT NULL,
        IsEnabled BIT NOT NULL CONSTRAINT DF_AppExternalGroupRoleMapping_IsEnabled DEFAULT (1),
        MappingDescription NVARCHAR(MAX) NULL,
        CreatedBy NVARCHAR(256) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AppExternalGroupRoleMapping_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy NVARCHAR(256) NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_AppExternalGroupRoleMapping_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AppExternalGroupRoleMapping_AppRole FOREIGN KEY (AppRoleId) REFERENCES openalex.AppRole(AppRoleId),
        CONSTRAINT CK_AppExternalGroupRoleMapping_MatchType CHECK (MatchType IN ('Exact','Contains','EndsWith'))
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_AppExternalGroupRoleMapping_Group_Role' AND object_id=OBJECT_ID(N'openalex.AppExternalGroupRoleMapping'))
BEGIN
    CREATE UNIQUE INDEX UX_AppExternalGroupRoleMapping_Group_Role ON openalex.AppExternalGroupRoleMapping(ExternalGroupValue, AppRoleId);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_AppRoleChangeAudit_TargetUser_CreatedAtUtc' AND object_id=OBJECT_ID(N'openalex.AppRoleChangeAudit'))
BEGIN
    CREATE INDEX IX_AppRoleChangeAudit_TargetUser_CreatedAtUtc ON openalex.AppRoleChangeAudit(TargetAppUserId, CreatedAtUtc);
END
GO
