using Microsoft.AspNetCore.Mvc;
using VinhKhanh.API.Data;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AnalyticsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> PostTrace([FromBody] TraceLog trace)
        {
            if (trace == null) return BadRequest();
            trace.TimestampUtc = DateTime.UtcNow;
            _db.TraceLogs.Add(trace);
            await _db.SaveChangesAsync();
            return Ok(trace);
        }

        [HttpGet("topPois")]
        public async Task<IActionResult> GetTopPois(int top = 10)
        {
            var q = _db.TraceLogs
                .GroupBy(t => t.PoiId)
                .Select(g => new { PoiId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top);

            return Ok(await q.ToListAsync());
        }

        [HttpGet("avg-duration")]
        public async Task<IActionResult> GetAvgDuration(int poiId)
        {
            var q = await _db.TraceLogs
                .Where(t => t.PoiId == poiId && t.DurationSeconds.HasValue)
                .Select(t => t.DurationSeconds.Value)
                .ToListAsync();

            if (!q.Any()) return Ok(new { poiId, avg = 0.0 });
            return Ok(new { poiId, avg = q.Average() });
        }

        [HttpGet("heatmap")]
        public async Task<IActionResult> GetHeatmap(int limit = 100)
        {
            var points = await _db.TraceLogs
                .OrderByDescending(t => t.TimestampUtc)
                .Take(limit)
                .Select(t => new { t.Latitude, t.Longitude })
                .ToListAsync();
            return Ok(points);
        }
    }
}
