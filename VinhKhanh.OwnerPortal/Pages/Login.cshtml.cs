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
                // Dòng này để ông soi lỗi ở cửa sổ Output nè
                Console.WriteLine($"DEBUG_API_RESPONSE: {body}");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // 1. Lấy UserId
                int userId = 0;
                if (root.TryGetProperty("userId", out var userIdProp))
                {
                    userId = userIdProp.GetInt32();
                }

                // 2. XỬ LÝ LỖI GetBoolean: Đọc isVerified một cách an toàn
                bool isVerified = false;
                if (root.TryGetProperty("isVerified", out var verifiedProp))
                {
                    // Nếu API trả về kiểu True/False chuẩn
                    if (verifiedProp.ValueKind == JsonValueKind.True) isVerified = true;
                    else if (verifiedProp.ValueKind == JsonValueKind.False) isVerified = false;
                    // Nếu API trả về kiểu số (1 là true, 0 là false)
                    else if (verifiedProp.ValueKind == JsonValueKind.Number) isVerified = verifiedProp.GetInt32() == 1;
                    // Nếu API trả về kiểu chữ ("true" hoặc "Approved")
                    else if (verifiedProp.ValueKind == JsonValueKind.String)
                    {
                        var val = verifiedProp.GetString()?.ToLower();
                        isVerified = (val == "true" || val == "approved" || val == "1");
                    }
                }

                // 🚫 Kiểm tra phê duyệt
                if (!isVerified)
                {
                    ModelState.AddModelError("", "❌ Tài khoản của bạn chưa được duyệt từ admin. Vui lòng liên hệ admin để được phê duyệt.");
                    return Page();
                }

                // Lưu Cookie và đăng nhập thành công
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
    }
}