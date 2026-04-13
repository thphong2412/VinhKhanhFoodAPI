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
            _apiKey = config.GetValue<string>("ApiKey") ?? "dev-key";
        }

        public async Task InvokeAsync(HttpContext context)
        {
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
