using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Json;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class RegisterModel : PageModel
    {
        private readonly IHttpClientFactory _factory;
        private readonly Microsoft.Extensions.Logging.ILogger<RegisterModel> _logger;

        public RegisterModel(IHttpClientFactory factory, Microsoft.Extensions.Logging.ILogger<RegisterModel> logger)
        {
            _factory = factory;
            _logger = logger;
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
            if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Password) || 
                string.IsNullOrEmpty(ShopName) || string.IsNullOrEmpty(ShopAddress) || 
                string.IsNullOrEmpty(Cccd))
            {
                ModelState.AddModelError("", "Vui lòng điền đầy đủ tất cả các trường");
                return Page();
            }

            var client = _factory.CreateClient("api");
            var req = new { Email = Email, Password = Password, ShopName = ShopName, ShopAddress = ShopAddress, Cccd = Cccd };

            try
            {
                _logger.LogInformation($"[Register] Sending request to API with Email: {Email}");
                var res = await client.PostAsJsonAsync("admin/auth/register-owner", req);
                _logger.LogInformation($"[Register] API Response Status: {res.StatusCode}");

                if (res.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"[Register] Success - redirecting to RegisterSuccess");
                    TempData["SuccessMessage"] = $"✓ Đăng ký thành công! Email: {Email}. Vui lòng chờ admin duyệt đơn đăng ký của bạn.";
                    return RedirectToPage("RegisterSuccess");
                }
                else
                {
                    var content = await res.Content.ReadAsStringAsync();
                    _logger.LogWarning($"[Register] Failed with status {res.StatusCode}: {content}");

                    if (content.Contains("email_exists"))
                        ModelState.AddModelError("", "Email này đã được đăng ký. Vui lòng sử dụng email khác.");
                    else if (content.Contains("missing"))
                        ModelState.AddModelError("", "Dữ liệu không hợp lệ. Vui lòng kiểm tra lại.");
                    else
                        ModelState.AddModelError("", $"Đăng ký thất bại: {content}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Register] Exception occurred");
                ModelState.AddModelError("", $"Lỗi: {ex.Message}");
            }

            return Page();
        }
    }
}
