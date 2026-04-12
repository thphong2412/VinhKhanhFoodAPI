using Microsoft.AspNetCore.Mvc;
using VinhKhanh.API.Data;
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

        public AudioController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
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

            return Ok(model);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var a = await _db.AudioFiles.FindAsync(id);
            if (a == null) return NotFound();
            _db.AudioFiles.Remove(a);
            await _db.SaveChangesAsync();
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

            return Ok(item);
        }
    }
}
