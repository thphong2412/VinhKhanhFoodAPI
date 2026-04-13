using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace VinhKhanh.OwnerPortal.Pages
{
    public class OwnerDashboardModel : PageModel
    {
        public int UserId { get; set; }
        public bool IsVerified { get; set; }

        public void OnGet(int userId)
        {
            UserId = userId;
            if (Request.Cookies.TryGetValue("owner_verified", out var v)) IsVerified = v == "1";
        }
    }
}
