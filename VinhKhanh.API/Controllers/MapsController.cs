using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;

namespace VinhKhanh.API.Controllers
{
    [Route("api/maps")]
    [ApiController]
    public class MapsController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public MapsController(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _config = config;
        }

        [HttpGet("runtime-config")]
        public IActionResult RuntimeConfig()
        {
            var token = _config["Mapbox:AccessToken"]
                        ?? _config["MapboxAccessToken"]
                        ?? _config["MAPBOX_ACCESS_TOKEN"]
                        ?? Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN")
                        ?? string.Empty;

            return Ok(new
            {
                mapboxAccessToken = token
            });
        }

        [HttpGet("offline-manifest")]
        public IActionResult OfflineManifest(string version = "q4-v1")
        {
            var wwwroot = Path.Combine(_env.ContentRootPath, "wwwroot");
            var packRoot = Path.Combine(wwwroot, "map-packs", version);
            if (!Directory.Exists(packRoot))
            {
                return NotFound(new { error = "map_pack_not_found", version });
            }

            var files = Directory.GetFiles(packRoot, "*", SearchOption.AllDirectories)
                .Select(f =>
                {
                    var rel = Path.GetRelativePath(wwwroot, f).Replace("\\", "/");
                    using var sha = SHA256.Create();
                    using var fs = System.IO.File.OpenRead(f);
                    var hash = Convert.ToHexString(sha.ComputeHash(fs));
                    var fi = new FileInfo(f);
                    return new
                    {
                        url = "/" + rel,
                        name = Path.GetFileName(f),
                        extension = Path.GetExtension(f),
                        size = fi.Length,
                        lastWriteUtc = fi.LastWriteTimeUtc,
                        sha256 = hash
                    };
                })
                .ToList();

            var totalBytes = files.Sum(f => (long)f.size);
            var suggestedEntryHtml = files.FirstOrDefault(f => string.Equals((string)f.extension, ".html", StringComparison.OrdinalIgnoreCase));
            var suggestedPmtiles = files.FirstOrDefault(f => string.Equals((string)f.extension, ".pmtiles", StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                version,
                area = "Quan4",
                provider = "Mapbox/PMTiles-ready",
                recommendedMode = "hybrid",
                totalAssets = files.Count,
                totalBytes,
                suggestedEntryHtml = suggestedEntryHtml?.url,
                suggestedPmtiles = suggestedPmtiles?.url,
                assets = files,
                generatedAtUtc = DateTime.UtcNow
            });
        }
    }
}
