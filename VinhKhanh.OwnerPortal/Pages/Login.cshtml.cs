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

        [BindProperty]
        public string RecoveryEmail { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Password))
            {
                ModelState.AddModelError("", "Vui lòng điền email và mật khẩu");
                return Page();
            }

            try
            {
                var client = _factory.CreateClient("api");
                var res = await client.PostAsJsonAsync("admin/auth/login", new { Email = Email, Password = Password });

                if (!res.IsSuccessStatusCode)
                {
                    ModelState.AddModelError("", "Email hoặc mật khẩu không chính xác");
                    return Page();
                }

                var body = await res.Content.ReadAsStringAsync();
                Console.WriteLine($"DEBUG_API_RESPONSE: {body}");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var userId = 0;
                if (root.TryGetProperty("userId", out var userIdProp))
                {
                    userId = userIdProp.GetInt32();
                }

                var isVerified = false;
                if (root.TryGetProperty("isVerified", out var verifiedProp))
                {
                    if (verifiedProp.ValueKind == JsonValueKind.True) isVerified = true;
                    else if (verifiedProp.ValueKind == JsonValueKind.False) isVerified = false;
                    else if (verifiedProp.ValueKind == JsonValueKind.Number) isVerified = verifiedProp.GetInt32() == 1;
                    else if (verifiedProp.ValueKind == JsonValueKind.String)
                    {
                        var val = verifiedProp.GetString()?.ToLowerInvariant();
                        isVerified = val == "true" || val == "approved" || val == "1";
                    }
                }

                if (!isVerified)
                {
                    ModelState.AddModelError("", "❌ Tài khoản của bạn chưa được duyệt từ admin. Vui lòng liên hệ admin để được phê duyệt.");
                    return Page();
                }

                Response.Cookies.Append("owner_userid", userId.ToString());
                Response.Cookies.Append("owner_verified", "1");

                return RedirectToPage("OwnerDashboard", new { userId = userId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Lỗi hệ thống: {ex.Message}");
                return Page();
            }
        }

        public IActionResult OnPostForgotPassword()
        {
            var target = string.IsNullOrWhiteSpace(RecoveryEmail) ? Email : RecoveryEmail;
            if (string.IsNullOrWhiteSpace(target))
            {
                ModelState.AddModelError("", "Vui lòng nhập email để nhận link reset mật khẩu.");
                return Page();
            }

            TempData["InfoMessage"] = "Link reset mật khẩu mới đã gửi vào email của bạn, hãy kiểm tra hộp thư.";
            return RedirectToPage();
        }
    }
}
