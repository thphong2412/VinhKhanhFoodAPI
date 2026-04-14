using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerDashboardModel : PageModel
    {
        public int UserId { get; set; }
        public bool IsVerified { get; set; }

        public void OnGet()
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
