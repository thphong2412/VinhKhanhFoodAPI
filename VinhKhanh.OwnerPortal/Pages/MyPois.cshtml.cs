using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using VinhKhanh.Shared;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class MyPoisModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        public List<PoiModel> Pois { get; set; }

        public MyPoisModel(IHttpClientFactory factory)
        {
            _factory = factory;
            Pois = new List<PoiModel>();
        }

        public async Task OnGetAsync()
        {
            if (!Request.Cookies.TryGetValue("owner_userid", out var v)) return;
            if (!int.TryParse(v, out var uid)) return;
            var client = _factory.CreateClient("api");
            var list = await client.GetFromJsonAsync<List<PoiModel>>($"api/poi?ownerId={uid}");
            Pois = list ?? new List<PoiModel>();
        }
    }
}
