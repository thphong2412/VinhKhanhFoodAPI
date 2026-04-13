using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class RegisterModel : PageModel
    {
        private readonly IHttpClientFactory _factory;

        public RegisterModel(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        [BindProperty]
        public string Email { get; set; }
        [BindProperty]
        public string Password { get; set; }
        [BindProperty]
        public string ShopName { get; set; }
        [BindProperty]
        public string ShopAddress { get; set; }
        [BindProperty]
        public string Cccd { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            var client = _factory.CreateClient("api");
            var req = new { Email = Email, Password = Password, ShopName = ShopName, ShopAddress = ShopAddress, Cccd = Cccd };
            var res = await client.PostAsJsonAsync("admin/auth/register-owner", req);
            if (res.IsSuccessStatusCode) return RedirectToPage("Login");
            ModelState.AddModelError("", "Đăng ký thất bại");
            return Page();
        }
    }
}
