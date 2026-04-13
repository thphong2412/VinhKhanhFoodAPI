using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VinhKhanh.Shared;

namespace VinhKhanh.Data
{
    public class PoiRepository
    {
        private bool _initialized = false;
        private readonly ILogger _logger;

        public PoiRepository(ILogger<PoiRepository> logger)
        {
            _logger = logger;
        }

        private async Task Init()
        {
            if (_initialized) return;

            try
            {
                await using var connection = new SqliteConnection(Constants.DatabasePath);
                await connection.OpenAsync();

                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS PoiModel (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT,
                        Category TEXT,
                        Latitude REAL,
                        Longitude REAL,
                        Radius REAL,
                        Priority INTEGER,
                        CooldownSeconds INTEGER,
                        ImageUrl TEXT,
                        QrCode TEXT,
                        IsSaved INTEGER
                    );";

                await cmd.ExecuteNonQueryAsync();
                _initialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing POI table");
                throw;
            }
        }

        public async Task<List<PoiModel>> ListAsync()
        {
            await Init();
            var results = new List<PoiModel>();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Category, Latitude, Longitude, Radius, Priority, CooldownSeconds, ImageUrl, QrCode, IsSaved FROM PoiModel";

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new PoiModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Category = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Latitude = reader.GetDouble(3),
                    Longitude = reader.GetDouble(4),
                    Radius = reader.GetDouble(5),
                    Priority = reader.GetInt32(6),
                    CooldownSeconds = reader.IsDBNull(7) ? 30 : reader.GetInt32(7),
                    ImageUrl = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    QrCode = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    IsSaved = !reader.IsDBNull(10) && reader.GetInt32(10) != 0
                });
            }

            return results;
        }

        public async Task<int> SaveAsync(PoiModel poi)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            if (poi.Id == 0)
            {
                cmd.CommandText = @"INSERT INTO PoiModel (Name, Category, Latitude, Longitude, Radius, Priority, CooldownSeconds, ImageUrl, QrCode, IsSaved)
                                     VALUES (@Name, @Category, @Latitude, @Longitude, @Radius, @Priority, @CooldownSeconds, @ImageUrl, @QrCode, @IsSaved);
                                     SELECT last_insert_rowid();";
            }
            else
            {
                cmd.CommandText = @"UPDATE PoiModel SET Name=@Name, Category=@Category, Latitude=@Latitude, Longitude=@Longitude, Radius=@Radius, Priority=@Priority, CooldownSeconds=@CooldownSeconds, ImageUrl=@ImageUrl, QrCode=@QrCode, IsSaved=@IsSaved WHERE Id=@Id;";
                cmd.Parameters.AddWithValue("@Id", poi.Id);
            }

            cmd.Parameters.AddWithValue("@Name", poi.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("@Category", poi.Category ?? string.Empty);
            cmd.Parameters.AddWithValue("@Latitude", poi.Latitude);
            cmd.Parameters.AddWithValue("@Longitude", poi.Longitude);
            cmd.Parameters.AddWithValue("@Radius", poi.Radius);
            cmd.Parameters.AddWithValue("@Priority", poi.Priority);
            cmd.Parameters.AddWithValue("@CooldownSeconds", poi.CooldownSeconds);
            cmd.Parameters.AddWithValue("@ImageUrl", poi.ImageUrl ?? string.Empty);
            cmd.Parameters.AddWithValue("@QrCode", poi.QrCode ?? string.Empty);
            cmd.Parameters.AddWithValue("@IsSaved", poi.IsSaved ? 1 : 0);

            var result = await cmd.ExecuteScalarAsync();
            if (poi.Id == 0 && result != null)
            {
                poi.Id = Convert.ToInt32(result);
            }

            return poi.Id;
        }

        public async Task DropTableAsync()
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE IF EXISTS PoiModel";
            await cmd.ExecuteNonQueryAsync();
            _initialized = false;
        }
    }
}
