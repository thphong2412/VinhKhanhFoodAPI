using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PoiController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PoiController(AppDbContext context)
        {
            _context = context;
        }

        // 1. GET: api/Poi (Lấy danh sách)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PoiModel>>> GetPois()
        {
            try
            {
                var q = _context.PointsOfInterest.AsQueryable();

                // Kiểm tra Admin bằng API Key
                var apiKey = HttpContext.Request.Headers["X-API-Key"].FirstOrDefault();
                var configuredKey = "admin123";
                var isAdminCaller = !string.IsNullOrEmpty(apiKey) && apiKey == configuredKey;

                if (!isAdminCaller)
                {
                    q = q.Where(p => p.IsPublished);
                }

                var ownerIdStr = HttpContext.Request.Query["ownerId"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ownerIdStr) && int.TryParse(ownerIdStr, out var ownerId))
                {
                    q = q.Where(p => p.OwnerId == ownerId);
                }

                var result = await q.ToListAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // 2. GET: api/Poi/{id} - FIX LỖI 404/405 TRANG CHI TIẾT VÀ SỬA
        [HttpGet("{id}")]
        public async Task<ActionResult<PoiModel>> GetPoiById(int id)
        {
            var poi = await _context.PointsOfInterest
                                    .Include(p => p.Contents)
                                    .FirstOrDefaultAsync(m => m.Id == id);

            if (poi == null) return NotFound();
            return Ok(poi);
        }

        // 3. PUT: api/Poi/{id} - Cập nhật thông tin
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePoi(int id, PoiModel model)
        {
            if (id != model.Id) return BadRequest("ID mismatch");

            var poi = await _context.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();

            // Cập nhật các trường dữ liệu
            poi.Name = model.Name;
            poi.Category = model.Category;
            poi.Latitude = model.Latitude;
            poi.Longitude = model.Longitude;
            poi.Radius = model.Radius;
            poi.Priority = model.Priority;
            poi.CooldownSeconds = model.CooldownSeconds;
            poi.ImageUrl = model.ImageUrl;
            poi.OwnerId = model.OwnerId;
            poi.IsPublished = model.IsPublished;

            await _context.SaveChangesAsync();
            return Ok(poi);
        }

        // 4. POST: api/Poi - Thêm mới
        [HttpPost]
        public async Task<ActionResult<PoiModel>> PostPoi(PoiModel poi)
        {
            _context.PointsOfInterest.Add(poi);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetPoiById), new { id = poi.Id }, poi);
        }

        // 5. DELETE: api/Poi/{id} - Xóa địa điểm
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePoi(int id)
        {
            var poi = await _context.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();

            _context.PointsOfInterest.Remove(poi);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}