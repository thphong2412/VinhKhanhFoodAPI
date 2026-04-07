using System.IO; // FIX LỖI: Path
using Microsoft.Maui.Storage; // FIX LỖI: FileSystem
namespace VinhKhanh.Data
{
    public static class Constants
    {
        public const string DatabaseFilename = "AppSQLite.db3";

        public static string DatabasePath =>
            $"Data Source={Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename)}";
    }
}