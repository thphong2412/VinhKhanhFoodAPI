using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddAuthentication("Cookies").AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
});

var apiBase = ResolveApiBaseUrl(builder.Configuration.GetValue<string>("ApiBaseUrl") ?? "http://localhost:5291/");
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBase);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// The API project hosts the SignalR hub; admin portal will connect as a client if needed.
// Do not map the API hub type from another project here to avoid cross-project dependency.

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=PoiAdmin}/{action=Index}/{id?}");

// Owner portal routes (owner registration/login/dashboard)
app.MapControllerRoute(
    name: "owner",
    pattern: "owner/{action=Dashboard}/{id?}",
    defaults: new { controller = "OwnerPortal" });

// Admin owners management
app.MapControllerRoute(
    name: "adminowners",
    pattern: "AdminOwners/{action=Index}/{id?}",
    defaults: new { controller = "AdminOwners" });

app.Run();

static string ResolveApiBaseUrl(string configured)
{
    var candidates = new List<string>();

    if (!string.IsNullOrWhiteSpace(configured))
    {
        candidates.Add(Normalize(configured));
    }

    candidates.Add("http://localhost:5291/");
    candidates.Add("http://localhost:35587/");

    foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (IsReachable(candidate))
        {
            return candidate;
        }
    }

    // fallback cuối cùng: giữ URL config để không phá behavior hiện có
    return Normalize(configured);
}

static bool IsReachable(string baseUrl)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var healthUrl = new Uri(new Uri(baseUrl), "health");
        using var res = client.GetAsync(healthUrl).GetAwaiter().GetResult();
        return res.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

static string Normalize(string baseUrl)
{
    var value = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:5291/" : baseUrl.Trim();
    if (!value.EndsWith('/')) value += "/";
    return value;
}
