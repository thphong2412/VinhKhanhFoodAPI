using Microsoft.AspNetCore.Mvc;
using VinhKhanh.API.Data;
using VinhKhanh.API.Models;
using VinhKhanh.Shared;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Net.Http.Json;

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

        [HttpPost("upload-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("file_required");
            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("invalid_image_type");

            try
            {
                var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                Directory.CreateDirectory(uploads);

                var ext = Path.GetExtension(file.FileName);
                var fileName = $"poi_reg_{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(uploads, fileName);

                await using (var stream = System.IO.File.Create(filePath))
                {
                    await file.CopyToAsync(stream);
                }

                var url = $"/uploads/{fileName}";
                return Ok(new { url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading registration image");
                return StatusCode(500, "upload_failed");
            }
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
                registration.RequestType = string.IsNullOrWhiteSpace(registration.RequestType)
                    ? "create"
                    : registration.RequestType.Trim().ToLowerInvariant();

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

                var reqType = (registration.RequestType ?? "create").Trim().ToLowerInvariant();

                if (reqType == "delete")
                {
                    if (!registration.TargetPoiId.HasValue)
                        return BadRequest("TargetPoiId is required for delete request");

                    var targetPoi = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == registration.TargetPoiId.Value);
                    if (targetPoi == null) return NotFound("Target POI not found");

                    var targetContents = await _db.PointContents.Where(c => c.PoiId == targetPoi.Id).ToListAsync();
                    var targetAudios = await _db.AudioFiles.Where(a => a.PoiId == targetPoi.Id).ToListAsync();
                    if (targetContents.Any()) _db.PointContents.RemoveRange(targetContents);
                    if (targetAudios.Any()) _db.AudioFiles.RemoveRange(targetAudios);
                    _db.PointsOfInterest.Remove(targetPoi);
                    await _db.SaveChangesAsync();

                    registration.Status = "approved";
                    registration.ReviewedAt = DateTime.UtcNow;
                    registration.ReviewNotes = request?.Notes;
                    registration.ReviewedBy = request?.ReviewedBy;
                    registration.ApprovedPoiId = targetPoi.Id;

                    _db.Update(registration);
                    await _db.SaveChangesAsync();

                    return Ok(new { success = true, message = "Delete request approved" });
                }

                if (reqType == "update")
                {
                    if (!registration.TargetPoiId.HasValue)
                        return BadRequest("TargetPoiId is required for update request");

                    var targetPoi = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == registration.TargetPoiId.Value);
                    if (targetPoi == null) return NotFound("Target POI not found");

                    targetPoi.Name = registration.Name;
                    targetPoi.Category = registration.Category;
                    targetPoi.Latitude = registration.Latitude;
                    targetPoi.Longitude = registration.Longitude;
                    targetPoi.Radius = registration.Radius;
                    targetPoi.Priority = registration.Priority;
                    targetPoi.CooldownSeconds = registration.CooldownSeconds;
                    targetPoi.ImageUrl = registration.ImageUrl;
                    targetPoi.WebsiteUrl = registration.WebsiteUrl;
                    if (!string.IsNullOrWhiteSpace(registration.QrCode)) targetPoi.QrCode = registration.QrCode;
                    targetPoi.IsPublished = true;
                    await _db.SaveChangesAsync();

                    await ApplyOwnerStagedPayloadAsync(registration, targetPoi);

                    if (!string.IsNullOrWhiteSpace(registration.ContentTitle)
                        || !string.IsNullOrWhiteSpace(registration.ContentSubtitle)
                        || !string.IsNullOrWhiteSpace(registration.ContentDescription)
                        || !string.IsNullOrWhiteSpace(registration.ContentPriceMin)
                        || !string.IsNullOrWhiteSpace(registration.ContentPriceMax)
                        || !string.IsNullOrWhiteSpace(registration.ContentOpenTime)
                        || !string.IsNullOrWhiteSpace(registration.ContentCloseTime)
                        || !string.IsNullOrWhiteSpace(registration.ContentPhoneNumber)
                        || !string.IsNullOrWhiteSpace(registration.ContentAddress)
                        || registration.ContentRating.HasValue)
                    {
                        var existingVi = await _db.PointContents
                            .FirstOrDefaultAsync(c => c.PoiId == targetPoi.Id && c.LanguageCode == "vi");

                        if (existingVi == null)
                        {
                            existingVi = new ContentModel
                            {
                                PoiId = targetPoi.Id,
                                LanguageCode = "vi"
                            };
                            _db.PointContents.Add(existingVi);
                        }

                        existingVi.Title = registration.ContentTitle;
                        existingVi.Subtitle = registration.ContentSubtitle;
                        existingVi.Description = registration.ContentDescription;
                        existingVi.PriceMin = registration.ContentPriceMin;
                        existingVi.PriceMax = registration.ContentPriceMax;
                        existingVi.Rating = registration.ContentRating ?? 0;
                        existingVi.OpenTime = registration.ContentOpenTime;
                        existingVi.CloseTime = registration.ContentCloseTime;
                        existingVi.PhoneNumber = registration.ContentPhoneNumber;
                        existingVi.Address = registration.ContentAddress;
                        existingVi.NormalizeCompositeFields();
                        await _db.SaveChangesAsync();
                    }

                    registration.Status = "approved";
                    registration.ReviewedAt = DateTime.UtcNow;
                    registration.ReviewNotes = request?.Notes;
                    registration.ReviewedBy = request?.ReviewedBy;
                    registration.ApprovedPoiId = targetPoi.Id;

                    _db.Update(registration);
                    await _db.SaveChangesAsync();

                    return Ok(new { success = true, poiId = targetPoi.Id, message = "Update request approved" });
                }

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

                if (!string.IsNullOrWhiteSpace(registration.ContentTitle)
                    || !string.IsNullOrWhiteSpace(registration.ContentSubtitle)
                    || !string.IsNullOrWhiteSpace(registration.ContentDescription)
                    || !string.IsNullOrWhiteSpace(registration.ContentPriceMin)
                    || !string.IsNullOrWhiteSpace(registration.ContentPriceMax)
                    || !string.IsNullOrWhiteSpace(registration.ContentOpenTime)
                    || !string.IsNullOrWhiteSpace(registration.ContentCloseTime)
                    || !string.IsNullOrWhiteSpace(registration.ContentPhoneNumber)
                    || !string.IsNullOrWhiteSpace(registration.ContentAddress)
                    || registration.ContentRating.HasValue)
                {
                    var content = new ContentModel
                    {
                        PoiId = poi.Id,
                        LanguageCode = "vi",
                        Title = registration.ContentTitle,
                        Subtitle = registration.ContentSubtitle,
                        Description = registration.ContentDescription,
                        PriceMin = registration.ContentPriceMin,
                        PriceMax = registration.ContentPriceMax,
                        Rating = registration.ContentRating ?? 0,
                        OpenTime = registration.ContentOpenTime,
                        CloseTime = registration.ContentCloseTime,
                        PhoneNumber = registration.ContentPhoneNumber,
                        Address = registration.ContentAddress,
                        IsTTS = false
                    };

                    content.NormalizeCompositeFields();
                    _db.PointContents.Add(content);
                    await _db.SaveChangesAsync();
                }

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

        private async Task ApplyOwnerStagedPayloadAsync(PoiRegistration registration, PoiModel targetPoi)
        {
            if (string.IsNullOrWhiteSpace(registration.ReviewNotes))
            {
                return;
            }

            var note = registration.ReviewNotes.Trim();

            // owner_audio_update::<lang>::<fileName>::<base64>
            if (note.StartsWith("owner_audio_update::", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var parts = note.Split(new[] { "::" }, 4, StringSplitOptions.None);
                    if (parts.Length != 4)
                    {
                        return;
                    }

                    var lang = string.IsNullOrWhiteSpace(parts[1]) ? "vi" : parts[1].Trim().ToLowerInvariant();
                    var sourceFileName = string.IsNullOrWhiteSpace(parts[2])
                        ? $"audio_{targetPoi.Id}_{Guid.NewGuid():N}.mp3"
                        : parts[2].Trim();
                    var bytes = Convert.FromBase64String(parts[3]);

                    var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    Directory.CreateDirectory(uploads);

                    var ext = Path.GetExtension(sourceFileName);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";

                    var cleanBaseName = Path.GetFileNameWithoutExtension(sourceFileName);
                    if (string.IsNullOrWhiteSpace(cleanBaseName)) cleanBaseName = "audio";
                    foreach (var invalid in Path.GetInvalidFileNameChars())
                    {
                        cleanBaseName = cleanBaseName.Replace(invalid, '_');
                    }
                    cleanBaseName = cleanBaseName.Replace(' ', '_');
                    if (cleanBaseName.Length > 40) cleanBaseName = cleanBaseName[..40];

                    var safeFileName = $"audio_{targetPoi.Id}_{Guid.NewGuid():N}_{cleanBaseName}{ext}";
                    var filePath = Path.Combine(uploads, safeFileName);
                    await System.IO.File.WriteAllBytesAsync(filePath, bytes);

                    _db.AudioFiles.Add(new AudioModel
                    {
                        PoiId = targetPoi.Id,
                        Url = $"/uploads/{safeFileName}",
                        LanguageCode = lang,
                        IsTts = false,
                        IsProcessed = true
                    });

                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply owner audio update for registration {RegistrationId}", registration.Id);
                }

                return;
            }

            // { eventType: "owner_translation_update", ... }
            if (note.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using var noteDoc = JsonDocument.Parse(note);
                    var root = noteDoc.RootElement;

                    if (!root.TryGetProperty("eventType", out var eventTypeProp))
                    {
                        return;
                    }

                    var eventType = eventTypeProp.GetString() ?? string.Empty;
                    if (string.Equals(eventType, "owner_tts_update", StringComparison.OrdinalIgnoreCase))
                    {
                        var ttsLanguageCode = root.TryGetProperty("languageCode", out var ttsLang)
                            ? (ttsLang.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                            : "vi";
                        var ttsUrl = root.TryGetProperty("url", out var ttsUrlProp)
                            ? (ttsUrlProp.GetString() ?? string.Empty).Trim()
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(ttsUrl))
                        {
                            return;
                        }

                        var existing = await _db.AudioFiles
                            .Where(a => a.PoiId == targetPoi.Id && a.IsTts && a.LanguageCode == ttsLanguageCode)
                            .ToListAsync();
                        if (existing.Any())
                        {
                            _db.AudioFiles.RemoveRange(existing);
                        }

                        _db.AudioFiles.Add(new AudioModel
                        {
                            PoiId = targetPoi.Id,
                            Url = ttsUrl,
                            LanguageCode = string.IsNullOrWhiteSpace(ttsLanguageCode) ? "vi" : ttsLanguageCode,
                            IsTts = true,
                            IsProcessed = true
                        });
                        await _db.SaveChangesAsync();
                        return;
                    }

                    if (string.Equals(eventType, "owner_tts_generate_all", StringComparison.OrdinalIgnoreCase))
                    {
                        var supportedLangs = new[] { "vi", "en", "fr", "ja", "ko", "zh" };
                        var contents = await _db.PointContents.Where(c => c.PoiId == targetPoi.Id).ToListAsync();
                        var viContent = contents.FirstOrDefault(c => c.LanguageCode == "vi");
                        if (viContent == null) return;

                        using var httpClient = new HttpClient { BaseAddress = new Uri($"{Request.Scheme}://{Request.Host}") };
                        foreach (var lang in supportedLangs)
                        {
                            var sourceContent = contents.FirstOrDefault(c => c.LanguageCode == lang) ?? viContent;
                            var text = sourceContent.Description ?? sourceContent.Title ?? targetPoi.Name;
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            var ttsReq = new
                            {
                                text,
                                lang,
                                voice = (string?)null
                            };
                            var ttsRes = await httpClient.PostAsJsonAsync("/api/audio/tts", ttsReq);
                            if (!ttsRes.IsSuccessStatusCode) continue;

                            var staticUrl = ttsRes.Headers.TryGetValues("X-Static-Url", out var vals)
                                ? vals.FirstOrDefault()
                                : null;
                            if (string.IsNullOrWhiteSpace(staticUrl)) continue;

                            var existingTts = await _db.AudioFiles.Where(a => a.PoiId == targetPoi.Id && a.IsTts && a.LanguageCode == lang).ToListAsync();
                            if (existingTts.Any()) _db.AudioFiles.RemoveRange(existingTts);

                            _db.AudioFiles.Add(new AudioModel
                            {
                                PoiId = targetPoi.Id,
                                Url = staticUrl,
                                LanguageCode = lang,
                                IsTts = true,
                                IsProcessed = true
                            });
                        }

                        await _db.SaveChangesAsync();
                        return;
                    }

                    if (!string.Equals(eventType, "owner_translation_update", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var translationLanguageCode = root.TryGetProperty("languageCode", out var langProp)
                        ? (langProp.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                        : string.Empty;

                    if (string.IsNullOrWhiteSpace(translationLanguageCode))
                    {
                        return;
                    }

                    var existingLang = await _db.PointContents
                        .FirstOrDefaultAsync(c => c.PoiId == targetPoi.Id && c.LanguageCode == translationLanguageCode);

                    if (existingLang == null)
                    {
                        existingLang = new ContentModel
                        {
                            PoiId = targetPoi.Id,
                            LanguageCode = translationLanguageCode
                        };
                        _db.PointContents.Add(existingLang);
                    }

                    if (root.TryGetProperty("title", out var titleProp)) existingLang.Title = titleProp.GetString();
                    if (root.TryGetProperty("subtitle", out var subtitleProp)) existingLang.Subtitle = subtitleProp.GetString();
                    if (root.TryGetProperty("description", out var descriptionProp)) existingLang.Description = descriptionProp.GetString();
                    if (root.TryGetProperty("priceMin", out var priceMinProp)) existingLang.PriceMin = priceMinProp.GetString();
                    if (root.TryGetProperty("priceMax", out var priceMaxProp)) existingLang.PriceMax = priceMaxProp.GetString();
                    if (root.TryGetProperty("openTime", out var openTimeProp)) existingLang.OpenTime = openTimeProp.GetString();
                    if (root.TryGetProperty("closeTime", out var closeTimeProp)) existingLang.CloseTime = closeTimeProp.GetString();
                    if (root.TryGetProperty("phoneNumber", out var phoneProp)) existingLang.PhoneNumber = phoneProp.GetString();
                    if (root.TryGetProperty("address", out var addressProp)) existingLang.Address = addressProp.GetString();

                    if (root.TryGetProperty("rating", out var ratingProp))
                    {
                        if (ratingProp.ValueKind == JsonValueKind.Number && ratingProp.TryGetDouble(out var numericRating))
                        {
                            existingLang.Rating = numericRating;
                        }
                        else if (ratingProp.ValueKind == JsonValueKind.String
                            && double.TryParse(ratingProp.GetString(), out var parsedRating))
                        {
                            existingLang.Rating = parsedRating;
                        }
                    }

                    existingLang.NormalizeCompositeFields();
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply owner translation update for registration {RegistrationId}", registration.Id);
                }
            }
        }

        [HttpPost("submit-update/{poiId}")]
        public async Task<IActionResult> SubmitUpdate(int poiId, [FromBody] PoiRegistration registration)
        {
            if (registration == null) return BadRequest("Registration is null");

            var target = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == poiId);
            if (target == null) return NotFound("Target POI not found");
            if (registration.OwnerId <= 0 || target.OwnerId != registration.OwnerId)
                return Unauthorized("Owner mismatch");

            // Tạm ẩn POI khi owner gửi yêu cầu sửa, chỉ hiện lại sau khi admin duyệt.
            if (target.IsPublished)
            {
                target.IsPublished = false;
                await _db.SaveChangesAsync();
            }

            registration.TargetPoiId = poiId;
            registration.RequestType = "update";
            return await SubmitPoi(registration);
        }

        [HttpPost("submit-delete/{poiId}")]
        public async Task<IActionResult> SubmitDelete(int poiId, [FromBody] PoiRegistration registration)
        {
            registration ??= new PoiRegistration();

            var target = await _db.PointsOfInterest.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
            if (target == null) return NotFound("Target POI not found");

            if (registration.OwnerId > 0 && target.OwnerId != registration.OwnerId)
                return Unauthorized("Owner mismatch");

            registration.OwnerId = target.OwnerId ?? registration.OwnerId;
            registration.Name = string.IsNullOrWhiteSpace(registration.Name) ? target.Name : registration.Name;
            registration.Category = string.IsNullOrWhiteSpace(registration.Category) ? target.Category : registration.Category;
            registration.Latitude = target.Latitude;
            registration.Longitude = target.Longitude;
            registration.Radius = target.Radius;
            registration.Priority = target.Priority;
            registration.CooldownSeconds = target.CooldownSeconds;
            registration.ImageUrl = target.ImageUrl;
            registration.WebsiteUrl = target.WebsiteUrl;
            registration.QrCode = target.QrCode;
            registration.TargetPoiId = poiId;
            registration.RequestType = "delete";

            return await SubmitPoi(registration);
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

                // Nếu từ chối update/delete thì khôi phục hiển thị POI mục tiêu
                var reqType = (registration.RequestType ?? "create").Trim().ToLowerInvariant();
                if ((reqType == "update" || reqType == "delete") && registration.TargetPoiId.HasValue)
                {
                    var targetPoi = await _db.PointsOfInterest.FirstOrDefaultAsync(p => p.Id == registration.TargetPoiId.Value);
                    if (targetPoi != null)
                    {
                        targetPoi.IsPublished = true;
                        await _db.SaveChangesAsync();
                    }
                }

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
