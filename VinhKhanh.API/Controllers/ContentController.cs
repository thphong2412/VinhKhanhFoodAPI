using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContentController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ContentController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("by-poi/{poiId}")]
        public async Task<IActionResult> GetByPoi(int poiId)
        {
            var list = await _db.PointContents.Where(c => c.PoiId == poiId).ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ContentModel model)
        {
            if (model == null) return BadRequest();
            _db.PointContents.Add(model);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetByPoi), new { poiId = model.PoiId }, model);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ContentModel model)
        {
            var existing = await _db.PointContents.FindAsync(id);
            if (existing == null) return NotFound();
            existing.LanguageCode = model.LanguageCode;
            existing.Title = model.Title;
            existing.Subtitle = model.Subtitle;
            existing.Description = model.Description;
            existing.AudioUrl = model.AudioUrl;
            existing.IsTTS = model.IsTTS;
            existing.PriceRange = model.PriceRange;
            existing.Rating = model.Rating;
            existing.OpeningHours = model.OpeningHours;
            existing.PhoneNumber = model.PhoneNumber;
            existing.Address = model.Address;
            existing.ShareUrl = model.ShareUrl;
            await _db.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _db.PointContents.FindAsync(id);
            if (existing == null) return NotFound();
            _db.PointContents.Remove(existing);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
