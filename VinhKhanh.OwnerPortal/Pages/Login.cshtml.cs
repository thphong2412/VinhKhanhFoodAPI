using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;
using System.Text.Json;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IHttpClientFactory _factory;

        public LoginModel(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        [BindProperty]
        public string Email { get; set; }
        [BindProperty]
        public string Password { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            var client = _factory.CreateClient("api");
            var res = await client.PostAsJsonAsync("admin/auth/login", new { Email = Email, Password = Password });
            if (!res.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Đăng nhập thất bại");
                return Page();
            }

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var userId = root.GetProperty("userId").GetInt32();
            var isVerified = root.GetProperty("isVerified").GetBoolean();

            Response.Cookies.Append("owner_userid", userId.ToString());
            Response.Cookies.Append("owner_verified", isVerified ? "1" : "0");

            return RedirectToPage("OwnerDashboard", new { userId = userId });
        }
    }
}
