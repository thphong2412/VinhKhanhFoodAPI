using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanhFoodAPI.Models;

namespace VinhKhanhFoodAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : ControllerBase
    {
        private readonly FoodDbContext _context;

        public SyncController(FoodDbContext context)
        {
            _context = context;
        }

        // API để mobile tải toàn bộ POI
        [HttpGet("poi")]
        public async Task<IActionResult> SyncPOI()
        {
            var pois = await _context.POIs.ToListAsync();
            return Ok(pois);
        }
    }
}