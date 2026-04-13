using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;

namespace VinhKhanh.API.Controllers
{
    [Route("admin/pois")]
    [ApiController]
    [Authorize(Policy = "AdminApi")]
    public class AdminPoisController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<VinhKhanh.API.Hubs.SyncHub> _hub;

        public AdminPoisController(AppDbContext db, Microsoft.AspNetCore.SignalR.IHubContext<VinhKhanh.API.Hubs.SyncHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> Pending()
        {
            var list = await _db.PointsOfInterest.Where(p => !p.IsPublished).OrderByDescending(p => p.Id).ToListAsync();
            return Ok(list);
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id)
        {
            var poi = await _db.PointsOfInterest.FindAsync(id);
            if (poi == null) return NotFound();
            poi.IsPublished = true;
            await _db.SaveChangesAsync();

            try
            {
                if (_hub != null)
                {
                    await _hub.Clients.All.SendCoreAsync("PoiCreated", new object[] { new { poi.Id, poi.Name, poi.Latitude, poi.Longitude, OwnerId = poi.OwnerId, IsPublished = poi.IsPublished } }, System.Threading.CancellationToken.None);
                }
            }
            catch { }

            return Ok(new { poi.Id, poi.IsPublished });
        }
    }
}
