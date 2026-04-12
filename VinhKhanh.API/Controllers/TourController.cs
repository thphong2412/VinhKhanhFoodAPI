using Microsoft.AspNetCore.Mvc;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;
using Microsoft.EntityFrameworkCore;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TourController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TourController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var tours = await _db.Set<VinhKhanh.Shared.TourModel>().ToListAsync();
            return Ok(tours);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] VinhKhanh.Shared.TourModel tour)
        {
            if (tour == null) return BadRequest();
            _db.Add(tour);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetAll), new { id = tour.Id }, tour);
        }
    }
}
