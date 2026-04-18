using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;

namespace VinhKhanh.API.Controllers
{
    [Route("/health")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        private static readonly DateTime _startedAtUtc = DateTime.UtcNow;
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public HealthController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var utcNow = DateTime.UtcNow;
            var requiredColumns = new[]
            {
                "ContentTitle",
                "ContentSubtitle",
                "ContentDescription",
                "ContentPriceMin",
                "ContentPriceMax",
                "ContentRating",
                "ContentOpenTime",
                "ContentCloseTime",
                "ContentPhoneNumber",
                "ContentAddress"
            };

            var provider = _db.Database.ProviderName ?? "unknown";
            var dbCanConnect = false;
            var availableColumns = new List<string>();
            var missingColumns = new List<string>();
            string schemaCheckError = string.Empty;

            try
            {
                dbCanConnect = await _db.Database.CanConnectAsync();

                if (dbCanConnect)
                {
                    availableColumns = await GetPoiRegistrationColumnsAsync(provider);
                    missingColumns = requiredColumns
                        .Where(c => !availableColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                schemaCheckError = ex.Message;
            }

            var status = dbCanConnect && missingColumns.Count == 0 && string.IsNullOrWhiteSpace(schemaCheckError)
                ? "ok"
                : "degraded";

            return Ok(new
            {
                status,
                timestamp = utcNow,
                startup = new
                {
                    environment = _env.EnvironmentName,
                    startedAtUtc = _startedAtUtc,
                    uptimeSeconds = Math.Max(0, (utcNow - _startedAtUtc).TotalSeconds)
                },
                database = new
                {
                    provider,
                    canConnect = dbCanConnect,
                    pointOfInterestCount = dbCanConnect ? await _db.PointsOfInterest.CountAsync() : 0,
                    pendingRegistrations = dbCanConnect ? await _db.PoiRegistrations.CountAsync(r => r.Status == "pending") : 0,
                    poiRegistrationSchema = new
                    {
                        expectedColumns = requiredColumns,
                        availableColumns,
                        missingColumns,
                        isValid = missingColumns.Count == 0,
                        error = schemaCheckError
                    }
                }
            });
        }

        [HttpGet("startup")]
        public Task<IActionResult> Startup() => Get();

        /// <summary>
        /// Trả về thời gian UTC của máy chủ (dùng đồng bộ client/debug nhanh).
        /// </summary>
        [HttpGet("time")]
        public IActionResult GetServerTimeUtc()
        {
            var utc = DateTime.UtcNow;
            return Ok(new
            {
                utcNow = utc,
                iso8601 = utc.ToString("o")
            });
        }

        private async Task<List<string>> GetPoiRegistrationColumnsAsync(string provider)
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            await using var cmd = conn.CreateCommand();
            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                cmd.CommandText = "PRAGMA table_info('PoiRegistrations');";
                var result = new List<string>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
                    result.Add(reader[1]?.ToString() ?? string.Empty);
                }

                return result.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            }

            cmd.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'PoiRegistrations';";

            var sqlResult = new List<string>();
            await using var sqlReader = await cmd.ExecuteReaderAsync();
            while (await sqlReader.ReadAsync())
            {
                sqlResult.Add(sqlReader[0]?.ToString() ?? string.Empty);
            }

            return sqlResult.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        }
    }
}
