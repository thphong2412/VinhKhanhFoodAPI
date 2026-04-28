using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    // ================================================================
    // [FEATURE: Đánh giá POI từ người dùng]
    // ----------------------------------------------------------------
    // Bảng DB:        PoiReviews   (xem AppDbContext.PoiReviews)
    // Model:          VinhKhanh.Shared/PoiReviewModel.cs
    //                  - Rating, Comment, LanguageCode, DeviceId, IsHidden
    //
    // App mobile:
    //   - Hiển thị reviews trong tab "Đánh giá" của POI detail.
    //     (xem VinhKhanh/Pages/MapPage.* — tab Review)
    //   - Gọi GET  api/poi-reviews/{poiId}
    //   - Gọi POST api/poi-reviews        khi user gửi đánh giá mới.
    //
    // Admin web (chỉ admin có thể ẩn đánh giá xúc phạm):
    //   - View:   VinhKhanh.AdminPortal/Views/PoiAdmin/Details.cshtml
    //              (mỗi review có nút "Ẩn"/"Hiện")
    //   - Action: VinhKhanh.AdminPortal/Controllers/PoiAdminController.cs
    //              ToggleReviewHidden(id, reviewId)
    //              → POST api/poi-reviews/{reviewId}/toggle-hidden
    // ================================================================
    [ApiController]
    [Route("api/poi-reviews")]
    public class PoiReviewsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public PoiReviewsController(AppDbContext db)
        {
            _db = db;
        }

        // [FEATURE: Lấy danh sách đánh giá] — đã filter IsHidden=false
        // → app sẽ KHÔNG thấy đánh giá đã bị admin ẩn.
        [HttpGet("{poiId:int}")]
        public async Task<IActionResult> GetByPoi(int poiId)
        {
            if (poiId <= 0) return BadRequest();

            var items = await _db.PoiReviews
                .AsNoTracking()
                .Where(x => x.PoiId == poiId && !x.IsHidden)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(100)
                .ToListAsync();

            return Ok(items);
        }

        // [FEATURE: Tạo đánh giá mới] — gọi từ app mobile
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PoiReviewModel review)
        {
            if (review == null) return BadRequest();
            if (review.PoiId <= 0) return BadRequest();

            review.Rating = Math.Clamp(review.Rating, 1, 5);
            review.Comment = review.Comment?.Trim() ?? string.Empty;
            review.LanguageCode = string.IsNullOrWhiteSpace(review.LanguageCode) ? "vi" : review.LanguageCode.Trim().ToLowerInvariant();
            review.CreatedAtUtc = DateTime.UtcNow;
            review.IsHidden = false;

            _db.PoiReviews.Add(review);
            await _db.SaveChangesAsync();

            return Ok(review);
        }

        // [FEATURE: Ẩn/Hiện đánh giá] — chỉ admin web gọi.
        // Admin caller: PoiAdminController.ToggleReviewHidden (form POST).
        [HttpPost("{reviewId:int}/toggle-hidden")]
        public async Task<IActionResult> ToggleHidden(int reviewId)
        {
            if (reviewId <= 0) return BadRequest();

            var review = await _db.PoiReviews.FirstOrDefaultAsync(x => x.Id == reviewId);
            if (review == null) return NotFound();

            review.IsHidden = !review.IsHidden;
            await _db.SaveChangesAsync();

            return Ok(review);
        }
    }
}
