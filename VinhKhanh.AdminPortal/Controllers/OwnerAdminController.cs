using Microsoft.AspNetCore.Mvc;

namespace VinhKhanh.AdminPortal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class OwnerAdminController : Controller
    {
        public IActionResult Index()
        {
            return RedirectToAction("Index", "AdminOwners");
        }
    }
}
