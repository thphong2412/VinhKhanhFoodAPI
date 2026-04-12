using Microsoft.AspNetCore.Mvc;

namespace VinhKhanh.API.Controllers
{
    [Route("/health")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok(new { status = "ok", timestamp = System.DateTime.UtcNow });
    }
}
