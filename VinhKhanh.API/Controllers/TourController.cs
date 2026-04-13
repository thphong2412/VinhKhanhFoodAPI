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
            var tours = await _db.Set<TourModel>().ToListAsync();
            return Ok(tours);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var tour = await _db.Set<TourModel>().FirstOrDefaultAsync(t => t.Id == id);
            if (tour == null) return NotFound();
            return Ok(tour);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TourModel tour)
        {
            if (tour == null) return BadRequest("Tour is null");
            if (string.IsNullOrWhiteSpace(tour.Name)) return BadRequest("Tour name is required");

            try
            {
                _db.Add(tour);
                await _db.SaveChangesAsync();
                return CreatedAtAction(nameof(GetById), new { id = tour.Id }, tour);
            }
            catch (Exception ex)
            {
                return BadRequest("Error creating tour: " + ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] TourModel tour)
        {
            if (tour == null) return BadRequest("Tour is null");
            if (tour.Id != id) return BadRequest("ID mismatch");

            try
            {
                var existing = await _db.Set<TourModel>().FirstOrDefaultAsync(t => t.Id == id);
                if (existing == null) return NotFound();

                existing.Name = tour.Name;
                existing.Description = tour.Description;
                existing.PoiIds = tour.PoiIds;
                existing.IsPublished = tour.IsPublished;

                _db.Update(existing);
                await _db.SaveChangesAsync();
                return Ok(existing);
            }
            catch (Exception ex)
            {
                return BadRequest("Error updating tour: " + ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var tour = await _db.Set<TourModel>().FirstOrDefaultAsync(t => t.Id == id);
                if (tour == null) return NotFound();

                _db.Remove(tour);
                await _db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest("Error deleting tour: " + ex.Message);
            }
        }
    }
}
