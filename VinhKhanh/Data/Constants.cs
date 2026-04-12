using System.IO;
namespace VinhKhanh.Data
{
    public static class Constants
    {
        public const string DatabaseFilename = "AppSQLite.db3";

        // Use a cross-platform application data folder so this library does not depend on MAUI
        public static string DatabasePath
        {
            get
            {
                var folder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                return $"Data Source={Path.Combine(folder, DatabaseFilename)}";
            }
        }
    }
}
