using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanhFoodAPI.Models;

namespace VinhKhanhFoodAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class POIController : ControllerBase
    {
        private readonly FoodDbContext _context;

        public POIController(FoodDbContext context)
        {
            _context = context;
        }

        // Lấy tất cả POI
        [HttpGet]
        public async Task<ActionResult<IEnumerable<POI>>> GetPOI()
        {
            return await _context.POIs.ToListAsync();
        }

        // Tìm POI gần vị trí user
        [HttpGet("nearby")]
        public async Task<IActionResult> GetNearby(double lat, double lng)
        {
            var pois = await _context.POIs.ToListAsync();

            var result = pois
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Latitude,
                    p.Longitude,
                    Distance = GetDistance(lat, lng, p.Latitude, p.Longitude)
                })
                .OrderBy(p => p.Distance)
                .FirstOrDefault(); // chỉ lấy POI gần nhất

            if (result == null)
                return NotFound();

            return Ok(result);
        
        }

        // Lấy POI theo ID
        [HttpGet("{id}")]
        public async Task<ActionResult<POI>> GetPOIById(int id)
        {
            var poi = await _context.POIs.FindAsync(id);

            if (poi == null)
            {
                return NotFound();
            }

            return poi;
        }

        // Hàm tính khoảng cách GPS (Haversine formula)
        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371; // bán kính trái đất km

            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) *
                Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) *
                Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }
        // Quét QR để lấy thông tin POI
        [HttpGet("qr/{code}")]
        public async Task<IActionResult> GetPOIByQR(string code)
        {
            var poi = await _context.POIs
                .FirstOrDefaultAsync(p => p.QRCode == code);

            if (poi == null)
            {
                return NotFound();
            }

            return Ok(poi);
        }
       
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePOI(int id, POI poi)
        {
            if (id != poi.Id)
                return BadRequest();

            _context.Entry(poi).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok(poi);
        }
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePOI(int id)
        {
            var poi = await _context.POIs.FindAsync(id);

            if (poi == null)
                return NotFound();

            _context.POIs.Remove(poi);
            await _context.SaveChangesAsync();

            return Ok();
        }
        [HttpPost]
        public async Task<ActionResult<POI>> CreatePOI(POI poi)
        {
            _context.POIs.Add(poi);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPOIById), new { id = poi.Id }, poi);
        }
    }
}
