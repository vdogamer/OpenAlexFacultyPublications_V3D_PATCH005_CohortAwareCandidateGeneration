/* OpenAlex Version 3C — Seed Default External Group Mappings */
SET NOCOUNT ON;
DECLARE @Seed TABLE (ExternalGroupValue NVARCHAR(512), MatchType NVARCHAR(50), RoleName NVARCHAR(100), MappingDescription NVARCHAR(MAX));
INSERT INTO @Seed VALUES
(N'uva-openalex-readers', N'Contains', N'Reader', N'Placeholder group for Reader access. Replace with real UVA group.'),
(N'uva-openalex-reviewers', N'Contains', N'Reviewer', N'Placeholder group for Reviewer access. Replace with real UVA group.'),
(N'uva-openalex-admins', N'Contains', N'Admin', N'Placeholder group for Admin access. Replace with real UVA group.');
MERGE openalex.AppExternalGroupRoleMapping AS target
USING (SELECT s.ExternalGroupValue,s.MatchType,r.AppRoleId,s.RoleName,s.MappingDescription FROM @Seed s JOIN openalex.AppRole r ON s.RoleName=r.RoleName) AS source
ON target.ExternalGroupValue=source.ExternalGroupValue AND target.AppRoleId=source.AppRoleId
WHEN MATCHED THEN UPDATE SET MatchType=source.MatchType,RoleName=source.RoleName,MappingDescription=source.MappingDescription,IsEnabled=1,UpdatedBy=N'V3C Seed',UpdatedAtUtc=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (ExternalGroupValue,MatchType,AppRoleId,RoleName,IsEnabled,MappingDescription,CreatedBy,CreatedAtUtc,UpdatedBy,UpdatedAtUtc)
VALUES (source.ExternalGroupValue,source.MatchType,source.AppRoleId,source.RoleName,1,source.MappingDescription,N'V3C Seed',SYSUTCDATETIME(),N'V3C Seed',SYSUTCDATETIME());
SELECT * FROM openalex.AppExternalGroupRoleMapping ORDER BY RoleName, ExternalGroupValue;
