using Microsoft.AspNetCore.Mvc;
using VinhKhanhFoodAPI.Models;

namespace VinhKhanhFoodAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly FoodDbContext _context;

        public AnalyticsController(FoodDbContext context)
        {
            _context = context;
        }

        [HttpPost("visit")]
        public async Task<IActionResult> LogVisit([FromBody] Visit visit)
        {
            var vietnamTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "SE Asia Standard Time"
            );

            visit.VisitTime = vietnamTime;

            _context.Visits.Add(visit);
            await _context.SaveChangesAsync();

            var result = new
            {
                visit.Id,
                visit.PoiId,
                VisitTime = visit.VisitTime.ToString("HH:mm:ss dd/MM/yyyy"),
                visit.DeviceId
            };

            return Ok(result);
        }
    }
}