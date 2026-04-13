using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddAuthentication("Cookies").AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
});

var apiBase = builder.Configuration.GetValue<string>("ApiBaseUrl") ?? "http://localhost:5291/";
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBase);
})
// In development allow untrusted dev certificates when calling https://localhost:7174
.ConfigurePrimaryHttpMessageHandler(() =>
{
    if (builder.Environment.IsDevelopment() && apiBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        } as HttpMessageHandler;
    }

    return new HttpClientHandler() as HttpMessageHandler;
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
