using System; // FIX LỖI: Exception, Convert
using System.Collections.Generic; // FIX LỖI: List
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VinhKhanh.Models;

namespace VinhKhanh.Data
{
    public class TagRepository
    {
        private bool _hasBeenInitialized = false;
        private readonly ILogger _logger;

        public TagRepository(ILogger<TagRepository> logger)
        {
            _logger = logger;
        }

        private async Task Init()
        {
            if (_hasBeenInitialized)
                return;

            try
            {
                await using var connection = new SqliteConnection(Constants.DatabasePath);
                await connection.OpenAsync();

                var createTableCmd = connection.CreateCommand();

                // Tạo bảng Tag
                createTableCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Tag (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT NOT NULL,
                        Color TEXT NOT NULL
                    );";
                await createTableCmd.ExecuteNonQueryAsync();

                // Tạo bảng trung gian ProjectsTags cho quan hệ n-n
                createTableCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ProjectsTags (
                        ProjectID INTEGER NOT NULL,
                        TagID INTEGER NOT NULL,
                        PRIMARY KEY(ProjectID, TagID)
                    );";
                await createTableCmd.ExecuteNonQueryAsync();

                _hasBeenInitialized = true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating tables");
                throw;
            }
        }

        public async Task<List<Tag>> ListAsync()
        {
            await Init();
            var tags = new List<Tag>();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Tag";

            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tags.Add(new Tag
                {
                    ID = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Color = reader.GetString(2)
                });
            }
            return tags;
        }

        public async Task<List<Tag>> ListAsync(int projectID)
        {
            await Init();
            var tags = new List<Tag>();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = @"
                SELECT t.*
                FROM Tag t
                JOIN ProjectsTags pt ON t.ID = pt.TagID
                WHERE pt.ProjectID = @ProjectID";
            selectCmd.Parameters.AddWithValue("@ProjectID", projectID);

            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tags.Add(new Tag
                {
                    ID = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Color = reader.GetString(2)
                });
            }
            return tags;
        }

        public async Task<Tag?> GetAsync(int id)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Tag WHERE ID = @id";
            selectCmd.Parameters.AddWithValue("@id", id);

            await using var reader = await selectCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Tag
                {
                    ID = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Color = reader.GetString(2)
                };
            }
            return null;
        }

        public async Task<int> SaveItemAsync(Tag item)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var saveCmd = connection.CreateCommand();
            if (item.ID == 0)
            {
                saveCmd.CommandText = @"
                    INSERT INTO Tag (Title, Color) VALUES (@Title, @Color);
                    SELECT last_insert_rowid();";
            }
            else
            {
                saveCmd.CommandText = @"
                    UPDATE Tag SET Title = @Title, Color = @Color WHERE ID = @ID";
                saveCmd.Parameters.AddWithValue("@ID", item.ID);
            }

            saveCmd.Parameters.AddWithValue("@Title", item.Title);
            saveCmd.Parameters.AddWithValue("@Color", item.Color);

            var result = await saveCmd.ExecuteScalarAsync();
            if (item.ID == 0)
            {
                item.ID = Convert.ToInt32(result);
            }
            return item.ID;
        }

        public async Task<int> SaveItemAsync(Tag item, int projectID)
        {
            await Init();
            await SaveItemAsync(item);

            var associated = await IsAssociated(item, projectID);
            if (associated) return 0;

            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var saveCmd = connection.CreateCommand();
            saveCmd.CommandText = "INSERT INTO ProjectsTags (ProjectID, TagID) VALUES (@projectID, @tagID)";
            saveCmd.Parameters.AddWithValue("@projectID", projectID);
            saveCmd.Parameters.AddWithValue("@tagID", item.ID);

            return await saveCmd.ExecuteNonQueryAsync();
        }

        private async Task<bool> IsAssociated(Tag item, int projectID)
        {
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM ProjectsTags WHERE ProjectID = @projectID AND TagID = @tagID";
            checkCmd.Parameters.AddWithValue("@projectID", projectID);
            checkCmd.Parameters.AddWithValue("@tagID", item.ID);

            int count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            return count != 0;
        }

        // 1. Hàm xóa Tag hoàn toàn khỏi Database
        public async Task<int> DeleteItemAsync(Tag item)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Tag WHERE ID = @id";
            deleteCmd.Parameters.AddWithValue("@id", item.ID);

            return await deleteCmd.ExecuteNonQueryAsync();
        }

        // 2. FIX LỖI ĐỎ: Hàm xóa liên kết giữa Tag và Project
        public async Task<int> DeleteItemAsync(Tag item, int projectID)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var deleteCmd = connection.CreateCommand();
            // Xóa ở bảng trung gian chứ không xóa bảng Tag chính
            deleteCmd.CommandText = "DELETE FROM ProjectsTags WHERE ProjectID = @projectID AND TagID = @tagID";
            deleteCmd.Parameters.AddWithValue("@projectID", projectID);
            deleteCmd.Parameters.AddWithValue("@tagID", item.ID);

            return await deleteCmd.ExecuteNonQueryAsync();
        }

        public async Task DropTableAsync()
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var dropTableCmd = connection.CreateCommand();
            dropTableCmd.CommandText = "DROP TABLE IF EXISTS Tag; DROP TABLE IF EXISTS ProjectsTags;";
            await dropTableCmd.ExecuteNonQueryAsync();
            _hasBeenInitialized = false;
        }
    }
}