using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VinhKhanh.API
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyMiddleware> _logger;
        private readonly string _apiKey;

        public ApiKeyMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            _apiKey = config.GetValue<string>("ApiKey") ?? "admin123";
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Allow authenticated JWT bearer callers
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader)
                && authHeader.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase)
                && context.User?.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            // Allow anonymous GETs for public endpoints; require API key for non-GET or sensitive paths
            if (string.Equals(context.Request.Method, "GET", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Allow owner portal endpoints (owner registration and owner POI management) without the API key.
            // In production, owner endpoints should be protected by proper auth (cookie/JWT) instead of this simple rule.
            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/owner", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Allow owner POI registration workflow without API key
            // (create/update/delete request submissions + image upload for pending registrations)
            if (path.StartsWith("/api/poiregistration", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Allow mobile/web app analytics ingest without API key.
            // Analytics payload is anonymous and already rate-limited in controller.
            if (path.StartsWith("/api/analytics", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Allow SignalR hub negotiate/connect without API key for public realtime sync clients.
            if (path.StartsWith("/sync", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Allow owner registration endpoint (POST /admin/auth/register-owner)
            if (path.Equals("/admin/auth/register-owner", System.StringComparison.OrdinalIgnoreCase) &&
                string.Equals(context.Request.Method, "POST", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Allow login endpoint (POST /admin/auth/login) without API key
            // Login must be publicly accessible; authorization happens inside the endpoint.
            if (path.Equals("/admin/auth/login", System.StringComparison.OrdinalIgnoreCase) &&
                string.Equals(context.Request.Method, "POST", System.StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-API-Key", out var extractedKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("API Key missing");
                return;
            }

            if (!string.Equals(extractedKey, _apiKey))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Invalid API Key");
                return;
            }

            await _next(context);
        }
    }
}
