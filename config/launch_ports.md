# Suggested Local Ports

Use these ports to avoid conflicts with older V1 apps.

```powershell
# Integrated web app
cd src/OpenAlex.Web
dotnet run --urls "http://localhost:5020"

# Operations dashboard from V1O, if also running
cd ..\..\..\OpenAlexInstitutionIngestion_V1O_ProductionLoggingDashboard
dotnet run --urls "http://localhost:5014"
```
