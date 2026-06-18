Set-Location "$PSScriptRoot\.."
dotnet sln OpenAlexFacultyPublications.sln add tests\OpenAlex.Tests\OpenAlex.Tests.csproj
dotnet restore
dotnet test tests\OpenAlex.Tests\OpenAlex.Tests.csproj
