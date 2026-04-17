using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerDashboardModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<OwnerDashboardModel> _logger;

        public int UserId { get; set; }
        public bool IsVerified { get; set; }
        public int TotalPois { get; set; }
        public int TotalHotPois { get; set; }
        public int TotalEnRouteUsers { get; set; }
        public int TotalActiveUsers { get; set; }
        public int TotalVisitedUsers { get; set; }
        public int TotalQrScans { get; set; }
        public List<PoiLiveStatsDto> TopOwnerPois { get; set; } = new();

        public OwnerDashboardModel(IHttpClientFactory factory, ILogger<OwnerDashboardModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) 
            {
                RedirectToPage("Login");
                return;
            }

            if (!int.TryParse(v, out var uid))
            {
                RedirectToPage("Login");
                return;
            }

            UserId = uid;
            if (Request.Cookies.TryGetValue("owner_verified", out var verified)) 
                IsVerified = verified == "1";

            try
            {
                var client = _factory.CreateClient("api");

                var ownerPois = await client.GetFromJsonAsync<List<PoiModel>>($"api/poi?ownerId={uid}") ?? new List<PoiModel>();
                var ownerPoiIds = ownerPois.Select(x => x.Id).ToHashSet();
                TotalPois = ownerPois.Count;

                var liveStats = await client.GetFromJsonAsync<List<PoiLiveStatsDto>>("api/analytics/poi-live-stats?top=200") ?? new List<PoiLiveStatsDto>();
                var ownerStats = liveStats.Where(x => ownerPoiIds.Contains(x.PoiId)).ToList();

                TotalHotPois = ownerStats.Count(x => x.IsHot);
                TotalEnRouteUsers = ownerStats.Sum(x => x.EnRouteUsers);
                TotalActiveUsers = ownerStats.Sum(x => x.ActiveUsers);
                TotalVisitedUsers = ownerStats.Sum(x => x.VisitedUsers);
                TotalQrScans = ownerStats.Sum(x => x.QrScanCount);

                TopOwnerPois = ownerStats
                    .OrderByDescending(x => x.ConversionScore)
                    .ThenByDescending(x => x.ActiveUsers)
                    .Take(5)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không tải được owner dashboard metrics");
            }
        }

        public IActionResult OnPostLogout()
        {
            Response.Cookies.Delete("owner_userid");
            Response.Cookies.Delete("owner_verified");
            TempData["Message"] = "Bạn đã đăng xuất.";
            return RedirectToPage("Login");
        }
    }
}
