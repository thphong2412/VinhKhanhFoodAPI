This project will use an on-disk Sqlite database when running in Development to simplify local testing.
If you want to use SQL Server instead, set ASPNETCORE_ENVIRONMENT=Production and configure the DefaultConnection in appsettings.json.
To initialize the Sqlite DB, run from project folder:
  dotnet ef migrations add Init --startup-project ..\VinhKhanh.API --project VinhKhanh.API
  dotnet ef database update --startup-project ..\VinhKhanh.API --project VinhKhanh.API
