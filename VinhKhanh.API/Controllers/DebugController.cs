using Microsoft.AspNetCore.Mvc;
using VinhKhanh.API.Data;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DebugController : ControllerBase
    {
        private readonly AppDbContext _db;

        public DebugController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("status")]
        public IActionResult Status()
        {
            var poiCount = _db.PointsOfInterest.Count();
            var publishedCount = _db.PointsOfInterest.Count(p => p.IsPublished);
            var users = _db.Users.Count();

            return Ok(new {
                message = "API is running",
                database = new {
                    pois_total = poiCount,
                    pois_published = publishedCount,
                    users = users
                }
            });
        }
    }
}
