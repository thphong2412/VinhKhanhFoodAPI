using System; // FIX LỖI: Exception, Convert
using System.Collections.Generic; // FIX LỖI: List
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VinhKhanh.Models;

namespace VinhKhanh.Data
{
    public class TaskRepository
    {
        private bool _hasBeenInitialized = false;
        private readonly ILogger _logger;

        public TaskRepository(ILogger<TaskRepository> logger)
        {
            _logger = logger;
        }

        private async Task Init()
        {
            if (_hasBeenInitialized)
                return;

            try
            {
                // Đảm bảo VinhKhanh.Constants.DatabasePath đã được định nghĩa
                await using var connection = new SqliteConnection(Constants.DatabasePath);
                await connection.OpenAsync();

                var createTableCmd = connection.CreateCommand();
                createTableCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Task (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT NOT NULL,
                        IsCompleted INTEGER NOT NULL,
                        ProjectID INTEGER NOT NULL
                    );";
                await createTableCmd.ExecuteNonQueryAsync();
                _hasBeenInitialized = true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating Task table");
                throw;
            }
        }

        public async Task<List<ProjectTask>> ListAsync()
        {
            await Init();
            var tasks = new List<ProjectTask>();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Task";

            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tasks.Add(new ProjectTask
                {
                    ID = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    IsCompleted = reader.GetBoolean(2),
                    ProjectID = reader.GetInt32(3)
                });
            }
            return tasks;
        }

        public async Task<List<ProjectTask>> ListAsync(int projectId)
        {
            await Init();
            var tasks = new List<ProjectTask>();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Task WHERE ProjectID = @projectId";
            selectCmd.Parameters.AddWithValue("@projectId", projectId);

            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tasks.Add(new ProjectTask
                {
                    ID = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    IsCompleted = reader.GetBoolean(2),
                    ProjectID = reader.GetInt32(3)
                });
            }
            return tasks;
        }

        public async Task<ProjectTask?> GetAsync(int id)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Task WHERE ID = @id";
            selectCmd.Parameters.AddWithValue("@id", id);

            await using var reader = await selectCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ProjectTask
                {
                    ID = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    IsCompleted = reader.GetBoolean(2),
                    ProjectID = reader.GetInt32(3)
                };
            }
            return null;
        }

        public async Task<int> SaveItemAsync(ProjectTask item)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var saveCmd = connection.CreateCommand();
            if (item.ID == 0)
            {
                saveCmd.CommandText = @"
                    INSERT INTO Task (Title, IsCompleted, ProjectID) VALUES (@title, @isCompleted, @projectId);
                    SELECT last_insert_rowid();";
            }
            else
            {
                saveCmd.CommandText = @"
                    UPDATE Task SET Title = @title, IsCompleted = @isCompleted, ProjectID = @projectId WHERE ID = @id";
                saveCmd.Parameters.AddWithValue("@id", item.ID);
            }

            saveCmd.Parameters.AddWithValue("@title", item.Title);
            saveCmd.Parameters.AddWithValue("@isCompleted", item.IsCompleted ? 1 : 0); // SQLite lưu bool dạng 0/1
            saveCmd.Parameters.AddWithValue("@projectId", item.ProjectID);

            var result = await saveCmd.ExecuteScalarAsync();
            if (item.ID == 0)
            {
                item.ID = Convert.ToInt32(result);
            }
            return item.ID;
        }

        public async Task<int> DeleteItemAsync(ProjectTask item)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Task WHERE ID = @id";
            deleteCmd.Parameters.AddWithValue("@id", item.ID);

            return await deleteCmd.ExecuteNonQueryAsync();
        }

        public async Task DropTableAsync()
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var dropTableCmd = connection.CreateCommand();
            dropTableCmd.CommandText = "DROP TABLE IF EXISTS Task";
            await dropTableCmd.ExecuteNonQueryAsync();
            _hasBeenInitialized = false;
        }
    }
}