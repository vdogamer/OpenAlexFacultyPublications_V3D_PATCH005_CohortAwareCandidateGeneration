/* OpenAlex Version 3D — Seed Default Execution Windows */
SET NOCOUNT ON;
IF OBJECT_ID(N'openalex.JobExecutionWindow', N'U') IS NULL
BEGIN
    RAISERROR('openalex.JobExecutionWindow does not exist. Run 160 first.',16,1); RETURN;
END
IF NOT EXISTS (SELECT 1 FROM openalex.JobExecutionWindow WHERE WindowName=N'Local Demo Permissive Window')
BEGIN
    INSERT INTO openalex.JobExecutionWindow
    (WindowName,WindowDescription,AppliesToJobName,IsEnabled,DaysOfWeekCsv,StartTimeLocal,EndTimeLocal,TimeZoneId,AllowFullLoad,MaxRequestsPerWindow,CreatedBy,UpdatedBy)
    VALUES
    (N'Local Demo Permissive Window',N'Permissive all-day local/test execution window. Replace with narrower production windows before enforcing.',NULL,1,N'Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday','00:00','23:59',N'Eastern Standard Time',0,10,N'V3D Seed',N'V3D Seed');
END
IF NOT EXISTS (SELECT 1 FROM openalex.JobExecutionWindow WHERE WindowName=N'Production Example: Weekday Evening TestLoad')
BEGIN
    INSERT INTO openalex.JobExecutionWindow
    (WindowName,WindowDescription,AppliesToJobName,IsEnabled,DaysOfWeekCsv,StartTimeLocal,EndTimeLocal,TimeZoneId,AllowFullLoad,MaxRequestsPerWindow,CreatedBy,UpdatedBy)
    VALUES
    (N'Production Example: Weekday Evening TestLoad',N'Example narrow production window. Disabled by default. Enable only after approval.',NULL,0,N'Monday,Tuesday,Wednesday,Thursday,Friday','19:00','22:00',N'Eastern Standard Time',0,2,N'V3D Seed',N'V3D Seed');
END
SELECT * FROM openalex.JobExecutionWindow ORDER BY IsEnabled DESC, WindowName;
