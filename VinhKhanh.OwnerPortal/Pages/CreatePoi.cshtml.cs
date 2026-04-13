using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class CreatePoiModel : PageModel
    {
        private readonly IHttpClientFactory _factory;

        [BindProperty]
        public string Name { get; set; }
        [BindProperty]
        public string Category { get; set; }
        [BindProperty]
        public double Latitude { get; set; }
        [BindProperty]
        public double Longitude { get; set; }

        public CreatePoiModel(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) return RedirectToPage("Login");
            if (!int.TryParse(v, out var uid)) return RedirectToPage("Login");

            var poi = new PoiModel { Name = Name, Category = Category, Latitude = Latitude, Longitude = Longitude, OwnerId = uid };
            var client = _factory.CreateClient("api");
            var res = await client.PostAsJsonAsync("api/poi", poi);
            if (!res.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Tạo POI thất bại: " + res.StatusCode);
                return Page();
            }
            return RedirectToPage("MyPois");
        }
    }
}
