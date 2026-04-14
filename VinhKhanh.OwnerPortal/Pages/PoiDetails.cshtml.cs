using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class PoiDetailsModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<PoiDetailsModel> _logger;

        public PoiModel Poi { get; set; }

        public PoiDetailsModel(IHttpClientFactory factory, ILogger<PoiDetailsModel> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) 
                return RedirectToPage("Login");

            if (!int.TryParse(v, out var uid)) 
                return RedirectToPage("Login");

            try
            {
                var client = _factory.CreateClient("api");
                var poi = await client.GetFromJsonAsync<PoiModel>($"api/poi/{id}");

                if (poi == null || poi.OwnerId != uid)
                {
                    return NotFound();
                }

                Poi = poi;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading POI details");
                return NotFound();
            }
        }
    }
}
