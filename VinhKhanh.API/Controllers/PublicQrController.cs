using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Controllers
{
    [ApiController]
    [Route("")]
    public class PublicQrController : ControllerBase
    {
        private readonly AppDbContext _db;

        public PublicQrController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("qr/{poiId:int}")]
        public async Task<IActionResult> ScanAndRedirect(int poiId, [FromQuery] string? lang = null)
        {
            var poi = await _db.PointsOfInterest.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
            if (poi == null)
            {
                return Content("POI không tồn tại.", "text/plain; charset=utf-8");
            }

            var normalizedLang = NormalizeLang(lang);
            var deviceId = GetOrCreateWebDeviceId();

            await LogEventAsync(new TraceLog
            {
                PoiId = poiId,
                DeviceId = deviceId,
                Latitude = 0,
                Longitude = 0,
                DurationSeconds = null,
                ExtraJson = JsonSerializer.Serialize(new
                {
                    @event = "qr_scan",
                    source = "web_public_qr",
                    lang = normalizedLang
                })
            });

            return Redirect($"/listen/{poiId}?lang={Uri.EscapeDataString(normalizedLang)}");
        }

        [HttpGet("listen/{poiId:int}")]
        public async Task<IActionResult> Listen(int poiId, [FromQuery] string? lang = null)
        {
            var poi = await _db.PointsOfInterest.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
            if (poi == null)
            {
                return Content("POI không tồn tại.", "text/plain; charset=utf-8");
            }

            var normalizedLang = NormalizeLang(lang);
            var contents = await _db.PointContents.AsNoTracking().Where(c => c.PoiId == poiId).ToListAsync();
            var audios = await _db.AudioFiles.AsNoTracking().Where(a => a.PoiId == poiId).ToListAsync();

            var selectedContent = contents.FirstOrDefault(c => string.Equals(c.LanguageCode, normalizedLang, StringComparison.OrdinalIgnoreCase))
                                  ?? contents.FirstOrDefault(c => string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                                  ?? contents.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));

            var selectedAudio = audios.FirstOrDefault(a => string.Equals(a.LanguageCode, normalizedLang, StringComparison.OrdinalIgnoreCase))
                                ?? audios.FirstOrDefault(a => string.Equals(a.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                                ?? audios.FirstOrDefault(a => string.Equals(a.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));

            var title = selectedContent?.Title ?? poi.Name ?? $"POI #{poi.Id}";
            var subtitle = selectedContent?.Subtitle ?? string.Empty;
            var description = selectedContent?.Description ?? "Nội dung đang được cập nhật.";
            var audioUrl = selectedAudio?.Url ?? selectedContent?.AudioUrl ?? string.Empty;
            var absoluteAudioUrl = string.IsNullOrWhiteSpace(audioUrl) ? string.Empty : ToAbsoluteUrl(audioUrl);

            var encodedTitle = System.Net.WebUtility.HtmlEncode(title);
            var encodedSubtitle = System.Net.WebUtility.HtmlEncode(subtitle);
            var encodedDescription = System.Net.WebUtility.HtmlEncode(description);
            var encodedAddress = System.Net.WebUtility.HtmlEncode(selectedContent?.Address ?? string.Empty);
            var encodedOpen = System.Net.WebUtility.HtmlEncode(selectedContent?.OpeningHours ?? string.Empty);
            var encodedPrice = System.Net.WebUtility.HtmlEncode(selectedContent?.PriceRange ?? string.Empty);
            var encodedLang = System.Net.WebUtility.HtmlEncode(normalizedLang);
            var encodedAudio = System.Net.WebUtility.HtmlEncode(absoluteAudioUrl);

            var html = $@"<!doctype html>
<html lang='vi'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width,initial-scale=1' />
<title>{encodedTitle}</title>
<style>
body {{ font-family: Arial, sans-serif; margin: 0; background: #f5f6f8; color: #1f2937; }}
.container {{ max-width: 760px; margin: 0 auto; padding: 20px; }}
.card {{ background:#fff; border:1px solid #e5e7eb; border-radius:12px; padding:16px; margin-bottom:12px; }}
h1 {{ margin: 0 0 6px; font-size: 1.5rem; }}
.sub {{ color:#6b7280; margin-bottom: 8px; }}
.meta {{ color:#4b5563; font-size:.95rem; margin:4px 0; }}
.btn {{ border:0; border-radius:8px; padding:10px 14px; cursor:pointer; font-weight:600; }}
.btn-primary {{ background:#2563eb; color:#fff; }}
.btn-secondary {{ background:#111827; color:#fff; margin-left:8px; }}
audio {{ width:100%; margin-top:10px; }}
.notice {{ background:#eef2ff; border:1px solid #c7d2fe; color:#1e3a8a; border-radius:8px; padding:10px; margin-top:10px; }}
</style>
</head>
<body>
<div class='container'>
  <div class='card'>
    <h1>{encodedTitle}</h1>
    <div class='sub'>{encodedSubtitle}</div>
    <div class='meta'><strong>Ngôn ngữ:</strong> {encodedLang}</div>
    {(string.IsNullOrWhiteSpace(encodedAddress) ? string.Empty : $"<div class='meta'><strong>Địa chỉ:</strong> {encodedAddress}</div>")}
    {(string.IsNullOrWhiteSpace(encodedOpen) ? string.Empty : $"<div class='meta'><strong>Giờ mở cửa:</strong> {encodedOpen}</div>")}
    {(string.IsNullOrWhiteSpace(encodedPrice) ? string.Empty : $"<div class='meta'><strong>Giá:</strong> {encodedPrice}</div>")}
  </div>
  <div class='card'>
    <p id='desc'>{encodedDescription}</p>
    <div>
      <button id='btnPlay' class='btn btn-primary'>Nghe thuyết minh</button>
      <button id='btnStop' class='btn btn-secondary'>Dừng</button>
    </div>
    {(string.IsNullOrWhiteSpace(encodedAudio) ? "<div class='notice'>Không có file audio sẵn, hệ thống sẽ dùng trình đọc của trình duyệt.</div>" : $"<audio id='audio' controls preload='metadata' src='{encodedAudio}'></audio>")}
  </div>
</div>
<script>
const poiId = {poiId};
const lang = '{encodedLang}';
const hasAudio = {(string.IsNullOrWhiteSpace(encodedAudio) ? "false" : "true")};
const audio = document.getElementById('audio');
const desc = document.getElementById('desc')?.innerText || '';
let started = false;

async function track(eventName, durationSeconds) {{
  try {{
    await fetch('/qr/track', {{
      method: 'POST',
      headers: {{ 'Content-Type': 'application/json' }},
      body: JSON.stringify({{
        poiId,
        event: eventName,
        lang,
        source: 'web_public_qr',
        durationSeconds: durationSeconds ?? null
      }})
    }});
  }} catch {{ }}
}}

document.getElementById('btnPlay')?.addEventListener('click', async () => {{
  if (!started) {{
    started = true;
    await track('listen_start', null);
  }}

  if (hasAudio && audio) {{
    try {{ await audio.play(); }} catch {{ }}
    return;
  }}

  if ('speechSynthesis' in window && desc.trim().length > 0) {{
    window.speechSynthesis.cancel();
    const u = new SpeechSynthesisUtterance(desc);
    u.lang = lang === 'vi' ? 'vi-VN' : (lang === 'en' ? 'en-US' : lang);
    u.onend = async () => {{ await track('listen_complete', null); }};
    window.speechSynthesis.speak(u);
  }}
}});

document.getElementById('btnStop')?.addEventListener('click', () => {{
  if (audio) {{ audio.pause(); }}
  if ('speechSynthesis' in window) {{ window.speechSynthesis.cancel(); }}
}});

if (audio) {{
  audio.addEventListener('ended', async () => {{
    await track('listen_complete', audio.duration || null);
  }});
}}
</script>
</body>
</html>";

            return Content(html, "text/html; charset=utf-8");
        }

        [HttpPost("qr/track")]
        public async Task<IActionResult> Track([FromBody] PublicQrTrackRequest request)
        {
            if (request == null || request.PoiId <= 0 || string.IsNullOrWhiteSpace(request.Event))
            {
                return BadRequest("invalid_payload");
            }

            var eventName = request.Event.Trim().ToLowerInvariant();
            var accepted = eventName is "listen_start" or "listen_complete" or "qr_scan";
            if (!accepted) return BadRequest("invalid_event");

            var trace = new TraceLog
            {
                PoiId = request.PoiId,
                DeviceId = GetOrCreateWebDeviceId(),
                Latitude = 0,
                Longitude = 0,
                DurationSeconds = request.DurationSeconds,
                ExtraJson = JsonSerializer.Serialize(new
                {
                    @event = eventName,
                    source = string.IsNullOrWhiteSpace(request.Source) ? "web_public_qr" : request.Source,
                    lang = NormalizeLang(request.Lang)
                })
            };

            await LogEventAsync(trace);
            return Ok(new { ok = true });
        }

        private async Task LogEventAsync(TraceLog trace)
        {
            trace.TimestampUtc = DateTime.UtcNow;
            _db.TraceLogs.Add(trace);
            await _db.SaveChangesAsync();
        }

        private string NormalizeLang(string? lang)
        {
            var l = (lang ?? "vi").Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(l) ? "vi" : l;
        }

        private string ToAbsoluteUrl(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            if (value.StartsWith('/'))
            {
                return baseUrl + value;
            }

            return baseUrl + "/" + value;
        }

        private string GetOrCreateWebDeviceId()
        {
            const string cookieName = "vk_web_device";
            if (Request.Cookies.TryGetValue(cookieName, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var created = "web-" + Guid.NewGuid().ToString("N");
            Response.Cookies.Append(cookieName, created, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
            return created;
        }
    }

    public class PublicQrTrackRequest
    {
        public int PoiId { get; set; }
        public string? Event { get; set; }
        public string? Lang { get; set; }
        public string? Source { get; set; }
        public double? DurationSeconds { get; set; }
    }
}
