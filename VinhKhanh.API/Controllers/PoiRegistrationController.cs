using Microsoft.AspNetCore.Mvc;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;
using VinhKhanh.Shared;
using Microsoft.EntityFrameworkCore;

namespace VinhKhanh.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PoiRegistrationController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PoiRegistrationController> _logger;

        public PoiRegistrationController(AppDbContext db, ILogger<PoiRegistrationController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Owner submits a new POI for approval
        /// </summary>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitPoi([FromBody] PoiRegistration registration)
        {
            if (registration == null) return BadRequest("Registration is null");
            if (string.IsNullOrWhiteSpace(registration.Name)) return BadRequest("POI name is required");

            try
            {
                registration.SubmittedAt = DateTime.UtcNow;
                registration.Status = "pending";

                _db.Add(registration);
                await _db.SaveChangesAsync();

                _logger.LogInformation("POI registration submitted by owner {OwnerId}: {PoiName}", 
                    registration.OwnerId, registration.Name);

                return CreatedAtAction(nameof(GetById), new { id = registration.Id }, registration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting POI registration");
                return BadRequest("Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Get all pending POI registrations (for admin)
        /// </summary>
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingRegistrations()
        {
            try
            {
                var registrations = await _db.PoiRegistrations
                    .Where(r => r.Status == "pending")
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                return Ok(registrations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending registrations");
                return BadRequest("Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Get all POI registrations for an owner
        /// </summary>
        [HttpGet("owner/{ownerId}")]
        public async Task<IActionResult> GetOwnerRegistrations(int ownerId)
        {
            try
            {
                var registrations = await _db.PoiRegistrations
                    .Where(r => r.OwnerId == ownerId)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                return Ok(registrations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching owner registrations");
                return BadRequest("Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Get single registration by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var registration = await _db.PoiRegistrations.FirstOrDefaultAsync(r => r.Id == id);
                if (registration == null) return NotFound();

                return Ok(registration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching registration");
                return BadRequest("Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Admin approves a POI registration and creates the actual POI
        /// </summary>
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApprovePoi(int id, [FromBody] ApprovalRequest request)
        {
            try
            {
                var registration = await _db.PoiRegistrations.FirstOrDefaultAsync(r => r.Id == id);
                if (registration == null) return NotFound("Registration not found");

                // Create the actual POI
                var poi = new PoiModel
                {
                    Name = registration.Name,
                    Category = registration.Category,
                    Latitude = registration.Latitude,
                    Longitude = registration.Longitude,
                    Radius = registration.Radius,
                    Priority = registration.Priority,
                    CooldownSeconds = registration.CooldownSeconds,
                    ImageUrl = registration.ImageUrl,
                    WebsiteUrl = registration.WebsiteUrl,
                    QrCode = registration.QrCode,
                    OwnerId = registration.OwnerId,
                    IsPublished = true,
                    IsSaved = false
                };

                _db.Add(poi);
                await _db.SaveChangesAsync();

                // Update registration
                registration.Status = "approved";
                registration.ApprovedPoiId = poi.Id;
                registration.ReviewedAt = DateTime.UtcNow;
                registration.ReviewNotes = request?.Notes;
                registration.ReviewedBy = request?.ReviewedBy;

                _db.Update(registration);
                await _db.SaveChangesAsync();

                _logger.LogInformation("POI registration {RegistrationId} approved. Created POI {PoiId}", id, poi.Id);

                return Ok(new { success = true, poiId = poi.Id, message = "POI approved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving POI registration");
                return BadRequest("Error: " + ex.Message);
            }
        }

        /// <summary>
        /// Admin rejects a POI registration
        /// </summary>
        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectPoi(int id, [FromBody] ApprovalRequest request)
        {
            try
            {
                var registration = await _db.PoiRegistrations.FirstOrDefaultAsync(r => r.Id == id);
                if (registration == null) return NotFound("Registration not found");

                registration.Status = "rejected";
                registration.ReviewedAt = DateTime.UtcNow;
                registration.ReviewNotes = request?.Notes ?? "Rejected by admin";
                registration.ReviewedBy = request?.ReviewedBy;

                _db.Update(registration);
                await _db.SaveChangesAsync();

                _logger.LogInformation("POI registration {RegistrationId} rejected", id);

                return Ok(new { success = true, message = "POI rejected" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting POI registration");
                return BadRequest("Error: " + ex.Message);
            }
        }
    }

    public class ApprovalRequest
    {
        public string? Notes { get; set; }
        public int? ReviewedBy { get; set; }
    }
}
