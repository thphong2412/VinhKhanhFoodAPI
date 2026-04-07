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

        // GET: api/Poi
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PoiModel>>> GetPois()
        {
            // Lấy danh sách điểm kèm theo nội dung thuyết minh đa ngôn ngữ
            return await _context.PointsOfInterest
                .Include(p => p.Contents)
                .ToListAsync();
        }

        // POST: api/Poi (Dùng để thêm điểm mới từ Web Admin sau này)
        [HttpPost]
        public async Task<ActionResult<PoiModel>> PostPoi(PoiModel poi)
        {
            _context.PointsOfInterest.Add(poi);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetPois), new { id = poi.Id }, poi);
        }
    }
}