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
            try
            {
                // Lấy danh sách điểm kèm theo nội dung thuyết minh đa ngôn ngữ
                // Nếu request có X-API-Key hợp lệ (admin), trả về tất cả POI; ngược lại chỉ trả POI đã publish
                var q = _context.PointsOfInterest.AsQueryable();
                var apiKey = HttpContext.Request.Headers["X-API-Key"].FirstOrDefault();
                var configuredKey = HttpContext.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>()?.GetValue<string>("ApiKey") ?? "admin123";
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
                System.Console.WriteLine($"Error in GetPois: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
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
                if (hub != null) await hub.Clients.All.SendCoreAsync("PoiUpdated", new object[] { new { poi.Id, poi.Name, poi.OwnerId } }, System.Threading.CancellationToken.None);
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
            // Determine whether this creation should be published immediately.
            // If caller provides admin API key, mark published immediately; owner submissions remain unpublished until admin approves.
            var apiKey = HttpContext.Request.Headers["X-API-Key"].FirstOrDefault();
            var configuredKey = HttpContext.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>()?.GetValue<string>("ApiKey") ?? "dev-key";
            if (!string.IsNullOrEmpty(apiKey) && apiKey == configuredKey)
            {
                poi.IsPublished = true;
            }

            _context.PointsOfInterest.Add(poi);
            await _context.SaveChangesAsync();
            // Broadcast new POI to connected clients
            try
            {
                var hub = HttpContext.RequestServices.GetService<Microsoft.AspNetCore.SignalR.IHubContext<VinhKhanh.API.Hubs.SyncHub>>();
                    if (hub != null)
                    {
                    await hub.Clients.All.SendCoreAsync("PoiCreated", new object[] { new { poi.Id, poi.Name, poi.Latitude, poi.Longitude, OwnerId = poi.OwnerId, IsPublished = poi.IsPublished } }, System.Threading.CancellationToken.None);
                    }
            }
            catch { }

            return CreatedAtAction(nameof(GetPois), new { id = poi.Id }, poi);
        }
    }
}