using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using VinhKhanh.API.Data;
using VinhKhanh.API.Hubs;
using VinhKhanh.Shared;
using Microsoft.EntityFrameworkCore;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<SyncHub> _hubContext;

        public AudioController(AppDbContext db, IWebHostEnvironment env, IHubContext<SyncHub> hubContext)
        {
            _db = db;
            _env = env;
            _hubContext = hubContext;
        }

        [HttpGet("by-poi/{poiId}")]
        public async Task<IActionResult> GetByPoi(int poiId)
        {
            var files = await _db.AudioFiles.Where(a => a.PoiId == poiId).ToListAsync();
            return Ok(files);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] int poiId, [FromForm] string language = "vi")
        {
            var file = Request.Form.Files.FirstOrDefault();
            if (file == null) return BadRequest("No file uploaded");

            var uploads = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(uploads);
            var fileName = $"audio_{poiId}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploads, fileName);
            using (var fs = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(fs);
            }

            var url = $"/uploads/{fileName}";
            var model = new AudioModel { PoiId = poiId, Url = url, LanguageCode = language, IsTts = false, IsProcessed = true };
            _db.AudioFiles.Add(model);
            await _db.SaveChangesAsync();

            // ✅ Broadcast audio upload to all connected clients
            try
            {
                await _hubContext.Clients.All.SendAsync("AudioUploaded", new 
                { 
                    id = model.Id,
                    poiId = model.PoiId, 
                    url = model.Url,
                    languageCode = model.LanguageCode,
                    isTts = model.IsTts,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return Ok(model);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var a = await _db.AudioFiles.FindAsync(id);
            if (a == null) return NotFound();

            var poiId = a.PoiId;
            _db.AudioFiles.Remove(a);
            await _db.SaveChangesAsync();

            // ✅ Broadcast audio deletion to all connected clients
            try
            {
                await _hubContext.Clients.All.SendAsync("AudioDeleted", new { id, poiId, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return NoContent();
        }

        // GET: api/audio/pending
        [HttpGet("pending")]
        public async Task<IActionResult> GetPending()
        {
            var list = await _db.AudioFiles.Where(a => !a.IsProcessed).ToListAsync();
            return Ok(list);
        }

        // POST: api/audio/process/{id}
        // Process a queued TTS audio item (mock implementation for POC)
        [HttpPost("process/{id}")]
        public async Task<IActionResult> Process(int id)
        {
            var item = await _db.AudioFiles.FindAsync(id);
            if (item == null) return NotFound();

            if (item.IsProcessed) return BadRequest("Already processed");

            // Mock processing: create a placeholder file under wwwroot/uploads
            var uploads = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(uploads);
            var fileName = $"tts_{id}.txt"; // placeholder text file for POC
            var filePath = Path.Combine(uploads, fileName);
            await System.IO.File.WriteAllTextAsync(filePath, $"TTS_PLACEHOLDER for audio {id} generated at {DateTime.UtcNow}");

            item.Url = $"/uploads/{fileName}";
            item.IsProcessed = true;
            _db.AudioFiles.Update(item);
            await _db.SaveChangesAsync();

            // ✅ Broadcast TTS processing completed
            try
            {
                await _hubContext.Clients.All.SendAsync("AudioProcessed", new 
                { 
                    id = item.Id,
                    poiId = item.PoiId,
                    url = item.Url,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"SignalR broadcast failed: {ex.Message}");
            }

            return Ok(item);
        }
    }
}
