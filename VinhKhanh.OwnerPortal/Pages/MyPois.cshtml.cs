using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class PoiRegistrationDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Status { get; set; }
        public int? ApprovedPoiId { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNotes { get; set; }
    }

    public class MyPoisModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<MyPoisModel> _logger;

        public List<PoiModel> ApprovedPois { get; set; } = new();
        public List<PoiRegistrationDto> PendingPois { get; set; } = new();
        public List<PoiRegistrationDto> RejectedPois { get; set; } = new();

        public MyPoisModel(IHttpClientFactory factory, ILogger<MyPoisModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) return;
            if (!int.TryParse(v, out var uid)) return;

            try
            {
                var client = _factory.CreateClient("api");

                // Get approved POIs
                var approvedList = await client.GetFromJsonAsync<List<PoiModel>>($"api/poi?ownerId={uid}");
                ApprovedPois = approvedList ?? new List<PoiModel>();

                // Get owner's POI registrations (pending, approved, rejected)
                var registrations = await client.GetFromJsonAsync<List<PoiRegistrationDto>>($"api/poiregistration/owner/{uid}");
                if (registrations != null)
                {
                    PendingPois = registrations.Where(r => r.Status == "pending").ToList();
                    RejectedPois = registrations.Where(r => r.Status == "rejected").ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading POIs");
            }
        }
    }
}
