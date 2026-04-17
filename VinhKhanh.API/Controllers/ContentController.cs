using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.API.Hubs;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContentController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<SyncHub> _hubContext;

        public ContentController(AppDbContext db, IHubContext<SyncHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
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
            model.NormalizeCompositeFields();
            _db.PointContents.Add(model);
            await _db.SaveChangesAsync();

            // ✅ Broadcast content creation
            try
            {
                await _hubContext.Clients.All.SendAsync("ContentCreated", new 
                { 
                    id = model.Id,
                    poiId = model.PoiId,
                    languageCode = model.LanguageCode,
                    title = model.Title,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return CreatedAtAction(nameof(GetByPoi), new { poiId = model.PoiId }, model);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ContentModel model)
        {
            var existing = await _db.PointContents.FindAsync(id);
            if (existing == null) return NotFound();
            model.NormalizeCompositeFields();
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

            // ✅ Broadcast content update
            try
            {
                await _hubContext.Clients.All.SendAsync("ContentUpdated", new 
                { 
                    id = existing.Id,
                    poiId = existing.PoiId,
                    languageCode = existing.LanguageCode,
                    title = existing.Title,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _db.PointContents.FindAsync(id);
            if (existing == null) return NotFound();

            var poiId = existing.PoiId;
            _db.PointContents.Remove(existing);
            await _db.SaveChangesAsync();

            // ✅ Broadcast content deletion
            try
            {
                await _hubContext.Clients.All.SendAsync("ContentDeleted", new { id, poiId, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return NoContent();
        }
    }
}
