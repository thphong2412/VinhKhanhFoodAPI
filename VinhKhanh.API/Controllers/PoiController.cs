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
            // Hỗ trợ lọc theo ownerId query param để owner chỉ xem POI của họ
            var q = _context.PointsOfInterest.AsQueryable();
            var ownerIdStr = HttpContext.Request.Query["ownerId"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ownerIdStr) && int.TryParse(ownerIdStr, out var ownerId))
            {
                q = q.Where(p => p.OwnerId == ownerId);
            }

            return await q.Include(p => p.Contents).ToListAsync();
        }

        // PUT: api/Poi/{id} - update a POI
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePoi(int id, PoiModel model)
        {
            var poi = await _context.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();
            poi.Name = model.Name;
            poi.Category = model.Category;
            poi.Latitude = model.Latitude;
            poi.Longitude = model.Longitude;
            poi.Radius = model.Radius;
            poi.Priority = model.Priority;
            poi.CooldownSeconds = model.CooldownSeconds;
            poi.ImageUrl = model.ImageUrl;
            poi.OwnerId = model.OwnerId;
            await _context.SaveChangesAsync();

            try
            {
                var hub = HttpContext.RequestServices.GetService<Microsoft.AspNetCore.SignalR.IHubContext<VinhKhanh.API.Hubs.SyncHub>>();
                if (hub != null) await hub.Clients.All.SendAsync("PoiUpdated", new { poi.Id, poi.Name, poi.OwnerId });
            }
            catch { }

            return Ok(poi);
        }

        // POST: api/Poi (Dùng để thêm điểm mới từ Web Admin sau này)
        [HttpPost]
        public async Task<ActionResult<PoiModel>> PostPoi(PoiModel poi)
        {
            // For POC we allow creating POI with OwnerId set by caller.
            // Validate OwnerId if present: ensure user exists and is verified
            if (poi.OwnerId.HasValue)
            {
                var user = await _context.Set<VinhKhanh.API.Models.User>().FindAsync(poi.OwnerId.Value);
                if (user == null) return BadRequest("invalid_owner");
                if (!user.IsVerified) return BadRequest("owner_not_verified");
            }
            _context.PointsOfInterest.Add(poi);
            await _context.SaveChangesAsync();
            // Broadcast new POI to connected clients
            try
            {
                var hub = HttpContext.RequestServices.GetService<Microsoft.AspNetCore.SignalR.IHubContext<VinhKhanh.API.Hubs.SyncHub>>();
                if (hub != null)
                {
                    await hub.Clients.All.SendAsync("PoiCreated", new { poi.Id, poi.Name, poi.Latitude, poi.Longitude, OwnerId = poi.OwnerId });
                }
            }
            catch { }

            return CreatedAtAction(nameof(GetPois), new { id = poi.Id }, poi);
        }
    }
}