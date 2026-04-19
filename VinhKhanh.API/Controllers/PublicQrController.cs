using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using VinhKhanh.API.Data;
using VinhKhanh.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using VinhKhanh.API.Hubs;

namespace VinhKhanh.API.Controllers
{
    [ApiController]
    [Route("")]
    public class PublicQrController : ControllerBase
    {
        private readonly IHubContext<SyncHub> _hubContext;
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<PublicQrController> _logger;

        private static readonly string[] UiLanguages =
        {
            "vi", "en", "fr", "ja", "ko", "zh", "th", "es", "ru"
        };

        public PublicQrController(AppDbContext db, IConfiguration config, IHttpClientFactory httpFactory, ILogger<PublicQrController> logger, IHubContext<SyncHub> hubContext)
        {
            _db = db;
            _config = config;
            _httpFactory = httpFactory;
            _logger = logger;
            _hubContext = hubContext;
        }

        [HttpGet("listen/{poiId:int}/generate-tts")]
        public async Task<IActionResult> GenerateTtsForListen(int poiId, [FromQuery] string? lang = null)
        {
            var normalizedLang = NormalizeLang(lang);
            _logger.LogInformation("[QR-LANG] generate_tts_request poiId={PoiId} lang={Lang}", poiId, normalizedLang);
            try
            {
                var poi = await _db.PointsOfInterest.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
                if (poi == null) return NotFound(new { ok = false, reason = "poi_not_found" });

                var contents = await _db.PointContents.AsNoTracking().Where(c => c.PoiId == poiId).ToListAsync();
                var selectedContent = await BuildLocalizedContentAsync(contents, normalizedLang);
                if (selectedContent == null || string.IsNullOrWhiteSpace(selectedContent.Description))
                {
                    _logger.LogWarning("[QR-LANG] generate_tts_no_content poiId={PoiId} lang={Lang}", poiId, normalizedLang);
                    return StatusCode(204);
                }

                var publicUrl = await EnsureBackendTtsAudioAsync(poiId, normalizedLang, selectedContent.Description);
                if (string.IsNullOrWhiteSpace(publicUrl))
                {
                    _logger.LogWarning("[QR-LANG] generate_tts_failed poiId={PoiId} lang={Lang}", poiId, normalizedLang);
                    return StatusCode(504, new { ok = false });
                }

                var absolute = ToAbsoluteUrl(publicUrl);
                _logger.LogInformation("[QR-LANG] generate_tts_ready poiId={PoiId} lang={Lang} url={Url}", poiId, normalizedLang, absolute);
                return Ok(new { ok = true, url = absolute });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QR-LANG] generate_tts_exception poiId={PoiId} lang={Lang}", poiId, normalizedLang);
                return StatusCode(500, new { ok = false, error = "exception" });
            }
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

            if (IsLoopbackHost(Request.Host.Host)
                && !string.IsNullOrWhiteSpace(poi.QrCode)
                && Uri.TryCreate(poi.QrCode, UriKind.Absolute, out var poiQrUri)
                && !IsLoopbackHost(poiQrUri.Host))
            {
                var rebuilt = new UriBuilder(poiQrUri)
                {
                    Path = $"/listen/{poiId}",
                    Query = $"lang={Uri.EscapeDataString(normalizedLang)}"
                };
                return Redirect(rebuilt.Uri.ToString());
            }

            return Redirect($"/listen/{poiId}?lang={Uri.EscapeDataString(normalizedLang)}");
        }

        [HttpGet("listen/{poiId:int}")]
        public async Task<IActionResult> Listen(int poiId, [FromQuery] string? lang = null)
        {
            var startedAt = DateTime.UtcNow;
            var normalizedLang = NormalizeLang(lang);
            _logger.LogInformation("[QR-LANG] listen_start poiId={PoiId} rawLang={RawLang} normalizedLang={Lang}", poiId, lang, normalizedLang);
            try
            {
                var poi = await _db.PointsOfInterest.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
                if (poi == null)
                {
                    _logger.LogWarning("[QR-LANG] listen_failed poiId={PoiId} lang={Lang} reason=poi_not_found", poiId, normalizedLang);
                    var safeMissingHtml = BuildSafeFallbackHtml(poiId, normalizedLang, "POI không tồn tại hoặc đã bị xóa.");
                    return Content(safeMissingHtml, "text/html; charset=utf-8");
                }

                var contents = await _db.PointContents.AsNoTracking().Where(c => c.PoiId == poiId).ToListAsync();
                var audios = await _db.AudioFiles.AsNoTracking().Where(a => a.PoiId == poiId).ToListAsync();

                var selectedContent = await BuildLocalizedContentAsync(contents, normalizedLang);

                // Prefer exact audio in requested language. If missing, try to generate TTS for the requested language
                var selectedAudio = audios.FirstOrDefault(a => string.Equals(a.LanguageCode, normalizedLang, StringComparison.OrdinalIgnoreCase));

                if (selectedAudio == null && selectedContent != null && !string.IsNullOrWhiteSpace(selectedContent.Description))
                {
                    var generatedUrl = await EnsureBackendTtsAudioAsync(poi.Id, normalizedLang, selectedContent.Description);
                    if (!string.IsNullOrWhiteSpace(generatedUrl))
                    {
                        selectedAudio = new AudioModel
                        {
                            PoiId = poi.Id,
                            Url = generatedUrl,
                            LanguageCode = normalizedLang,
                            IsTts = true,
                            IsProcessed = true
                        };
                        _logger.LogInformation("[QR-LANG] backend_tts_generated poiId={PoiId} lang={Lang} url={Url}", poiId, normalizedLang, generatedUrl);
                    }
                    else
                    {
                        _logger.LogWarning("[QR-LANG] backend_tts_not_generated poiId={PoiId} lang={Lang}", poiId, normalizedLang);
                    }
                }

                // If still missing, fallback to English or Vietnamese existing audio files
                if (selectedAudio == null)
                {
                    selectedAudio = audios.FirstOrDefault(a => string.Equals(a.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                                    ?? audios.FirstOrDefault(a => string.Equals(a.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase));
                }

                var title = selectedContent?.Title ?? poi.Name ?? $"POI #{poi.Id}";
                var subtitle = selectedContent?.Subtitle ?? string.Empty;
                var description = selectedContent?.Description ?? "Nội dung đang được cập nhật.";
                var audioUrl = selectedAudio?.Url ?? selectedContent?.AudioUrl ?? string.Empty;
                var absoluteAudioUrl = ResolvePublicAudioUrl(audioUrl, poi, normalizedLang);

                var encodedTitle = System.Net.WebUtility.HtmlEncode(title);
            var encodedSubtitle = System.Net.WebUtility.HtmlEncode(subtitle);
            var encodedDescription = System.Net.WebUtility.HtmlEncode(description);
            var encodedAddress = System.Net.WebUtility.HtmlEncode(selectedContent?.Address ?? string.Empty);
            var encodedOpen = System.Net.WebUtility.HtmlEncode(selectedContent?.OpeningHours ?? string.Empty);
            var encodedPrice = System.Net.WebUtility.HtmlEncode(selectedContent?.GetNormalizedPriceRangeDisplay() ?? string.Empty);
            var encodedLang = System.Net.WebUtility.HtmlEncode(normalizedLang);
                var encodedAudio = System.Net.WebUtility.HtmlEncode(absoluteAudioUrl);

                var languageOptions = BuildLanguageOptions(normalizedLang);
                var encodedOptions = string.Join(string.Empty, languageOptions.Select(opt =>
                    $"<option value='{System.Net.WebUtility.HtmlEncode(opt.Code)}' {(opt.IsSelected ? "selected" : string.Empty)}>{System.Net.WebUtility.HtmlEncode(opt.Label)}</option>"));

                _logger.LogInformation("[QR-LANG] listen_ready poiId={PoiId} lang={Lang} hasAudio={HasAudio} elapsedMs={Elapsed}",
                poiId,
                normalizedLang,
                !string.IsNullOrWhiteSpace(absoluteAudioUrl),
                (DateTime.UtcNow - startedAt).TotalMilliseconds);

            var html = $@"<!doctype html>
<html lang='en'>
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
.lang-picker {{ display:flex; align-items:center; gap:8px; margin:10px 0 0; flex-wrap:wrap; }}
.lang-picker select {{ border:1px solid #d1d5db; border-radius:8px; padding:8px 10px; min-width:220px; }}
.btn {{ border:0; border-radius:8px; padding:10px 14px; cursor:pointer; font-weight:600; }}
.btn-primary {{ background:#2563eb; color:#fff; }}
.btn-secondary {{ background:#111827; color:#fff; margin-left:8px; }}
audio {{ width:100%; margin-top:10px; }}
.notice {{ background:#eef2ff; border:1px solid #c7d2fe; color:#1e3a8a; border-radius:8px; padding:10px; margin-top:10px; }}
.status-badge {{ display:inline-block; margin-top:10px; padding:8px 12px; border-radius:999px; font-size:.9rem; font-weight:600; background:#E8F0FE; color:#1A73E8; }}
.status-badge.hidden {{ display:none; }}
</style>
</head>
<body>
<div class='container'>
  <div class='card'>
    <h1>{encodedTitle}</h1>
    <div class='sub'>{encodedSubtitle}</div>
    <div class='meta'><strong>Language:</strong> {encodedLang}</div>
    <div class='lang-picker'>
      <label for='langSelect'><strong>Choose language:</strong></label>
      <select id='langSelect'>
        {encodedOptions}
      </select>
      <input id='customLang' type='text' placeholder='e.g.: de, it, ar...' maxlength='10' />
      <button id='btnApplyLang' class='btn btn-secondary' type='button'>Change language</button>
    </div>
    <div id='langSwitchStatus' class='status-badge hidden'>Translating / generating TTS...</div>
    {(string.IsNullOrWhiteSpace(encodedAddress) ? string.Empty : $"<div class='meta'><strong>Address:</strong> {encodedAddress}</div>")}
    {(string.IsNullOrWhiteSpace(encodedOpen) ? string.Empty : $"<div class='meta'><strong>Opening hours:</strong> {encodedOpen}</div>")}
    {(string.IsNullOrWhiteSpace(encodedPrice) ? string.Empty : $"<div class='meta'><strong>Price:</strong> {encodedPrice}</div>")}
  </div>
  <div class='card'>
    <p id='desc'>{encodedDescription}</p>
    <div>
      <button id='btnPlay' class='btn btn-primary'>Play narration</button>
      <button id='btnStop' class='btn btn-secondary'>Stop</button>
    </div>
    {(string.IsNullOrWhiteSpace(encodedAudio) ? "<div class='notice'>No pre-generated audio available; browser TTS will be used.</div>" : $"<audio id='audio' controls preload='metadata' src='{encodedAudio}'></audio>")}
  </div>
</div>
<script>
const poiId = {poiId};
const lang = '{encodedLang}';
let hasAudio = {(string.IsNullOrWhiteSpace(encodedAudio) ? "false" : "true")};
let audio = document.getElementById('audio');
const langSelect = document.getElementById('langSelect');
const customLang = document.getElementById('customLang');
const btnApplyLang = document.getElementById('btnApplyLang');
const langSwitchStatus = document.getElementById('langSwitchStatus');
const desc = document.getElementById('desc')?.innerText || '';
let started = false;
let playStartedAtMs = 0;
let speechStartMs = 0;
let latestLat = 0;
let latestLng = 0;

if (navigator.geolocation) {{
  navigator.geolocation.getCurrentPosition(
    (pos) => {{
      latestLat = Number(pos?.coords?.latitude || 0);
      latestLng = Number(pos?.coords?.longitude || 0);
    }},
    () => {{ }},
    {{ enableHighAccuracy: false, timeout: 2500, maximumAge: 60000 }}
  );
}}

function showLangSwitchStatus() {{
  if (!langSwitchStatus) return;
  langSwitchStatus.classList.remove('hidden');
}}

langSelect?.addEventListener('change', () => {{
  const selected = langSelect.value || 'vi';
  showLangSwitchStatus();
  const target = `/listen/${{poiId}}?lang=${{encodeURIComponent(selected)}}`;
  window.location.href = target;
}});

btnApplyLang?.addEventListener('click', () => {{
  const value = (customLang?.value || '').trim().toLowerCase();
  if (!value) return;
  showLangSwitchStatus();
  const target = `/listen/${{poiId}}?lang=${{encodeURIComponent(value)}}`;
  window.location.href = target;
}});

customLang?.addEventListener('keydown', (e) => {{
  if (e.key === 'Enter') {{
    e.preventDefault();
    btnApplyLang?.click();
  }}
}});

async function track(eventName, durationSeconds, mode) {{
  try {{
    await fetch('/qr/track', {{
      method: 'POST',
      headers: {{ 'Content-Type': 'application/json' }},
      body: JSON.stringify({{
        poiId,
        event: eventName,
        lang,
        source: 'web_public_qr',
        durationSeconds: durationSeconds ?? null,
        latitude: latestLat,
        longitude: latestLng,
        mode: mode || null
      }})
    }});
  }} catch {{ }}
}}

document.getElementById('btnPlay')?.addEventListener('click', async () => {{
  if (!started) {{
    started = true;
  }}
  playStartedAtMs = Date.now();
  await track('listen_start', null, hasAudio ? 'audio' : 'speech');

  // If we already have a generated audio file, play it
  if (hasAudio && audio) {{
    await track('audio_play', null, 'audio');
    try {{ await audio.play(); }} catch {{ }}
    return;
  }}

  // Try to ask backend to generate TTS for the selected language before falling back to browser TTS
  if (!hasAudio && desc.trim().length > 0) {{
    try {{
      if (langSwitchStatus) langSwitchStatus.classList.remove('hidden');

      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 15000);
      const resp = await fetch(`/listen/${{poiId}}/generate-tts?lang=${{encodeURIComponent(lang)}}`, {{ signal: controller.signal }});
      clearTimeout(timeout);

      if (resp && resp.ok) {{
        const body = await resp.json();
        if (body && body.ok && body.url) {{
          // create or replace audio element and play
          let audioElem = document.getElementById('audio');
          const card = document.getElementById('desc')?.parentElement;
          if (!audioElem) {{
            audioElem = document.createElement('audio');
            audioElem.id = 'audio';
            audioElem.controls = true;
            audioElem.preload = 'metadata';
            if (card) card.appendChild(audioElem);
          }}
          try {{
            audioElem.src = body.url;
            // update flags and trackers
            hasAudio = true;
            await track('audio_play', null, 'audio');
            try {{ await audioElem.play(); }} catch {{ }}
            if (langSwitchStatus) langSwitchStatus.classList.add('hidden');
            return;
          }} catch {{ }}
        }}
      }}
    }} catch (err) {{
      // ignore and fall back to browser TTS
    }} finally {{
      try {{ if (langSwitchStatus) langSwitchStatus.classList.add('hidden'); }} catch {{ }}
    }}
  }}

  // Fallback: use browser speechSynthesis with selected locale
  if ('speechSynthesis' in window && desc.trim().length > 0) {{
    try {{ window.speechSynthesis.cancel(); }} catch {{ }}
    const u = new SpeechSynthesisUtterance(desc);
    u.lang = toSpeechLocale(lang);
    speechStartMs = Date.now();
    await track('tts_play', null, 'speech');
    u.onend = async () => {{
      const elapsed = speechStartMs > 0 ? Math.max(1, Math.round((Date.now() - speechStartMs) / 1000)) : null;
      await track('listen_complete', elapsed, 'speech');
      speechStartMs = 0;
    }};
    window.speechSynthesis.speak(u);
  }}
}});

window.addEventListener('load', async () => {{
  try {{
    const playBtn = document.getElementById('btnPlay');
    if (playBtn) playBtn.click();
  }} catch {{ }}
}});

document.getElementById('btnStop')?.addEventListener('click', () => {{
  if (audio) {{ audio.pause(); }}
  if ('speechSynthesis' in window) {{ window.speechSynthesis.cancel(); }}
  if (playStartedAtMs > 0) {{
    const elapsed = Math.max(1, Math.round((Date.now() - playStartedAtMs) / 1000));
    track('listen_complete', elapsed, hasAudio ? 'audio' : 'speech');
  }}
  playStartedAtMs = 0;
  speechStartMs = 0;
}});

if (audio) {{
  audio.addEventListener('play', async () => {{
    playStartedAtMs = Date.now();
  }});

  audio.addEventListener('ended', async () => {{
    const elapsed = audio.currentTime > 0
      ? Math.max(1, Math.round(audio.currentTime))
      : (playStartedAtMs > 0 ? Math.max(1, Math.round((Date.now() - playStartedAtMs) / 1000)) : null);
    await track('listen_complete', elapsed, 'audio');
    playStartedAtMs = 0;
  }});

  audio.addEventListener('error', async () => {{
    if ('speechSynthesis' in window && desc.trim().length > 0) {{
      window.speechSynthesis.cancel();
      const u = new SpeechSynthesisUtterance(desc);
      u.lang = toSpeechLocale(lang);
      window.speechSynthesis.speak(u);
    }}
  }});
}}

function toSpeechLocale(language) {{
  const l = (language || '').toLowerCase();
  if (l.startsWith('vi')) return 'vi-VN';
  if (l.startsWith('en')) return 'en-US';
  if (l.startsWith('fr')) return 'fr-FR';
  if (l.startsWith('ja')) return 'ja-JP';
  if (l.startsWith('ko')) return 'ko-KR';
  if (l.startsWith('zh')) return 'zh-CN';
  if (l.startsWith('th')) return 'th-TH';
  if (l.startsWith('es')) return 'es-ES';
  if (l.startsWith('ru')) return 'ru-RU';
  return l;
}}
</script>
</body>
</html>";

                return Content(html, "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QR-LANG] listen_failed poiId={PoiId} lang={Lang}", poiId, normalizedLang);
                var safeHtml = BuildSafeFallbackHtml(poiId, normalizedLang);
                return Content(safeHtml, "text/html; charset=utf-8");
            }
        }

        private string BuildSafeFallbackHtml(int poiId, string lang, string? message = null)
        {
            var safeLang = System.Net.WebUtility.HtmlEncode(NormalizeLang(lang));
            var safeMessage = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(message)
                ? "Không tải được nội dung ngôn ngữ này"
                : message);
            return $@"<!doctype html><html><head><meta charset='utf-8' /><meta name='viewport' content='width=device-width,initial-scale=1' />
<title>QR Listen</title><style>body{{font-family:Arial,sans-serif;padding:20px;background:#f8fafc;color:#111827}}.box{{max-width:680px;margin:0 auto;background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:16px}}a{{color:#2563eb}}</style></head>
<body><div class='box'><h2>{safeMessage}</h2><p>Hệ thống đang tự fallback sang English.</p>
<p><a href='/listen/{poiId}?lang=en'>Mở English</a></p><p><small>Lang hiện tại: {safeLang}</small></p></div></body></html>";
        }

        [HttpPost("qr/track")]
        public async Task<IActionResult> Track([FromBody] PublicQrTrackRequest request)
        {
            if (request == null || request.PoiId <= 0 || string.IsNullOrWhiteSpace(request.Event))
            {
                return BadRequest("invalid_payload");
            }

            var eventName = request.Event.Trim().ToLowerInvariant();
            var accepted = eventName is "listen_start" or "listen_complete" or "qr_scan" or "tts_play" or "audio_play";
            if (!accepted) return BadRequest("invalid_event");

            var lat = request.Latitude ?? 0;
            var lng = request.Longitude ?? 0;
            if (double.IsNaN(lat) || lat < -90 || lat > 90) lat = 0;
            if (double.IsNaN(lng) || lng < -180 || lng > 180) lng = 0;

            var trace = new TraceLog
            {
                PoiId = request.PoiId,
                DeviceId = GetOrCreateWebDeviceId(),
                Latitude = lat,
                Longitude = lng,
                DurationSeconds = request.DurationSeconds,
                ExtraJson = JsonSerializer.Serialize(new
                {
                    @event = eventName,
                    source = string.IsNullOrWhiteSpace(request.Source) ? "web_public_qr" : request.Source,
                    lang = NormalizeLang(request.Lang),
                    mode = string.IsNullOrWhiteSpace(request.Mode) ? null : request.Mode.Trim().ToLowerInvariant()
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
            try
            {
                // Broadcast analytics event to connected admin/portal clients
                var payload = new
                {
                    trace.Id,
                    trace.PoiId,
                    trace.DeviceId,
                    trace.Latitude,
                    trace.Longitude,
                    trace.DurationSeconds,
                    trace.TimestampUtc,
                    Extra = trace.ExtraJson
                };

                await _hubContext.Clients.All.SendAsync("TraceLogged", payload);
                _logger?.LogInformation("[TRACE] Trace saved and broadcast poiId={PoiId} device={DeviceId} event={Extra}", trace.PoiId, trace.DeviceId, trace.ExtraJson);
            }
            catch
            {
                // ignore broadcast failures
            }
        }

        private string NormalizeLang(string? lang)
        {
            var l = (lang ?? "vi").Trim().ToLowerInvariant();
            if (l.Contains('-')) l = l.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? l;
            if (l.Contains('_')) l = l.Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? l;
            if (string.Equals(l, "vn", StringComparison.OrdinalIgnoreCase)) l = "vi";
            if (string.Equals(l, "viet", StringComparison.OrdinalIgnoreCase)) l = "vi";
            if (string.Equals(l, "eng", StringComparison.OrdinalIgnoreCase)) l = "en";
            return string.IsNullOrWhiteSpace(l) ? "vi" : l;
        }

        private List<(string Code, string Label, bool IsSelected)> BuildLanguageOptions(string currentLang)
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["vi"] = "Tiếng Việt",
                ["en"] = "English",
                ["fr"] = "Français",
                ["ja"] = "日本語",
                ["ko"] = "한국어",
                ["zh"] = "中文",
                ["th"] = "ไทย",
                ["es"] = "Español",
                ["ru"] = "Русский"
            };

            var result = UiLanguages
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(code =>
                {
                    var label = labels.TryGetValue(code, out var l) ? l : code;
                    return (Code: code, Label: label, IsSelected: string.Equals(code, currentLang, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            if (!result.Any(x => x.IsSelected))
            {
                result.Insert(0, (currentLang, currentLang, true));
            }

            return result;
        }

        private async Task<ContentModel?> BuildLocalizedContentAsync(List<ContentModel> contents, string targetLang)
        {
            var exact = contents.FirstOrDefault(c => string.Equals(c.LanguageCode, targetLang, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            var source = contents.FirstOrDefault(c => string.Equals(c.LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
                         ?? contents.FirstOrDefault(c => string.Equals(c.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase))
                         ?? contents.FirstOrDefault();
            if (source == null)
            {
                return null;
            }

            var sourceLang = NormalizeLang(source.LanguageCode);
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }

            var titleTask = TranslateTextWithFallbackAsync(source.Title, sourceLang, targetLang);
            var subtitleTask = TranslateTextWithFallbackAsync(source.Subtitle, sourceLang, targetLang);
            var descriptionTask = TranslateTextWithFallbackAsync(source.Description, sourceLang, targetLang);
            var addressTask = TranslateTextWithFallbackAsync(source.Address, sourceLang, targetLang);

            await Task.WhenAll(titleTask, subtitleTask, descriptionTask, addressTask);

            var translated = new ContentModel
            {
                Id = source.Id,
                PoiId = source.PoiId,
                LanguageCode = targetLang,
                Title = titleTask.Result,
                Subtitle = subtitleTask.Result,
                Description = descriptionTask.Result,
                Address = addressTask.Result,
                PriceRange = source.PriceRange,
                Rating = source.Rating,
                OpeningHours = source.OpeningHours,
                PhoneNumber = source.PhoneNumber,
                ShareUrl = source.ShareUrl,
                AudioUrl = source.AudioUrl,
                IsTTS = true
            };

            return translated;
        }

        private async Task<string?> TranslateTextWithFallbackAsync(string? text, string sourceLang, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var geminiTranslated = await TryTranslateWithGeminiAsync(text, sourceLang, targetLang);
            if (!string.IsNullOrWhiteSpace(geminiTranslated) && !IsSameText(geminiTranslated, text))
                return geminiTranslated;

            var freeTranslated = await TryTranslateWithFreeApiAsync(text, sourceLang, targetLang);
            return string.IsNullOrWhiteSpace(freeTranslated) ? text : freeTranslated;
        }

        private async Task<string?> TryTranslateWithGeminiAsync(string text, string sourceLang, string targetLang)
        {
            var apiKey = _config["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey)) return null;

            try
            {
                var model = _config["Gemini:Model"] ?? "gemini-1.5-flash";
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                var prompt = $"Translate plain text from {sourceLang} to {targetLang}. Return only translated text without markdown: {text}";

                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };

                var client = _httpFactory.CreateClient();
                var body = JsonSerializer.Serialize(payload);
                var res = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
                if (!res.IsSuccessStatusCode) return null;

                var resBody = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resBody);
                return doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString()
                    ?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> TryTranslateWithFreeApiAsync(string text, string sourceLang, string targetLang)
        {
            try
            {
                var src = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang;
                var client = _httpFactory.CreateClient();
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(src)}&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}";
                var res = await client.GetAsync(url);
                if (!res.IsSuccessStatusCode) return null;

                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return null;

                var segments = doc.RootElement[0];
                if (segments.ValueKind != JsonValueKind.Array) return null;

                var sb = new StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                    var part = segment[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
                }

                var result = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSameText(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)) return true;
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> EnsureBackendTtsAudioAsync(int poiId, string lang, string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return null;

                var normalizedLang = NormalizeLang(lang);
                var safeText = text.Trim();
                if (safeText.Length > 700)
                {
                    safeText = safeText[..700];
                }

                var cacheRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "tts-cache", "custom", normalizedLang);
                Directory.CreateDirectory(cacheRoot);
                CleanupCustomTtsCache(cacheRoot);

                var key = ComputeMd5($"{poiId}:{normalizedLang}:{safeText}");
                var fileName = $"qr_custom_{poiId}_{normalizedLang}_{key[..10]}.mp3";
                var filePath = Path.Combine(cacheRoot, fileName);
                var publicUrl = $"/tts-cache/custom/{normalizedLang}/{fileName}";

                if (!System.IO.File.Exists(filePath))
                {
                    // Allow configurable timeout (seconds) and a simple retry loop to improve chances of generation
                    var timeoutSecondsRaw = _config["PublicQr:TtsGenerationTimeoutSeconds"] ?? _config["GoogleTts:GenerationTimeoutSeconds"];
                    var timeoutSeconds = int.TryParse(timeoutSecondsRaw, out var tsec) && tsec > 0 ? tsec : 20;
                    var maxAttempts = 2;

                    for (var attempt = 1; attempt <= maxAttempts && !System.IO.File.Exists(filePath); attempt++)
                    {
                        using var ttsTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                        _logger.LogInformation("[QR-LANG] tts_generation_attempt poiId={PoiId} lang={Lang} attempt={Attempt}/{MaxAttempts}", poiId, normalizedLang, attempt, maxAttempts);
                        var generated = await TryGenerateTtsAudioAsync(safeText, normalizedLang, filePath, ttsTimeoutCts.Token);
                        if (!generated.Success)
                        {
                            _logger.LogWarning("[QR-LANG] tts_generation_failed poiId={PoiId} lang={Lang} attempt={Attempt} reason={Reason}", poiId, normalizedLang, attempt, generated.Error);
                            // small delay between attempts
                            if (attempt < maxAttempts)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(1));
                            }
                        }
                        else
                        {
                            _logger.LogInformation("[QR-LANG] tts_generation_succeeded poiId={PoiId} lang={Lang} attempt={Attempt}", poiId, normalizedLang, attempt);
                        }
                    }

                    if (!System.IO.File.Exists(filePath))
                    {
                        _logger.LogWarning("[QR-LANG] tts_generation_unavailable poiId={PoiId} lang={Lang}", poiId, normalizedLang);
                        return null;
                    }
                }

                return publicUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QR-LANG] EnsureBackendTtsAudioAsync exception poiId={PoiId} lang={Lang}", poiId, lang);
                return null;
            }
        }

        private void CleanupCustomTtsCache(string cacheRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
                {
                    return;
                }

                var ttlHours = Math.Clamp(GetCustomTtsCacheTtlHours(), 1, 24 * 60);
                var maxFiles = Math.Clamp(GetCustomTtsCacheMaxFiles(), 10, 5000);
                var ttlCutoffUtc = DateTime.UtcNow.AddHours(-ttlHours);

                var files = new DirectoryInfo(cacheRoot)
                    .GetFiles("*.mp3", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                foreach (var file in files.Where(f => f.LastWriteTimeUtc < ttlCutoffUtc))
                {
                    TryDeleteFile(file);
                }

                files = new DirectoryInfo(cacheRoot)
                    .GetFiles("*.mp3", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                if (files.Count <= maxFiles)
                {
                    return;
                }

                foreach (var file in files.Skip(maxFiles))
                {
                    TryDeleteFile(file);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch
            {
            }
        }

        private int GetCustomTtsCacheTtlHours()
        {
            var raw = _config["GoogleTts:CustomCacheTtlHours"]
                      ?? _config["PublicQr:TtsCacheTtlHours"]
                      ?? _config["PUBLIC_QR_TTS_CACHE_TTL_HOURS"]
                      ?? Environment.GetEnvironmentVariable("PUBLIC_QR_TTS_CACHE_TTL_HOURS");

            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 168;
        }

        private int GetCustomTtsCacheMaxFiles()
        {
            var raw = _config["GoogleTts:CustomCacheMaxFilesPerLang"]
                      ?? _config["PublicQr:TtsCacheMaxFilesPerLang"]
                      ?? _config["PUBLIC_QR_TTS_CACHE_MAX_FILES_PER_LANG"]
                      ?? Environment.GetEnvironmentVariable("PUBLIC_QR_TTS_CACHE_MAX_FILES_PER_LANG");

            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 300;
        }

        private async Task<(bool Success, int StatusCode, string? Error)> TryGenerateTtsAudioAsync(string text, string lang, string outputPath, CancellationToken cancellationToken)
        {
            var fallbackError = await TryGenerateWithGoogleTtsAsync(text, lang, outputPath, cancellationToken);
            if (fallbackError == null)
            {
                return (true, 200, null);
            }

            return (false, 502, $"google_tts_failed={fallbackError}");
        }

        private async Task<string?> TryGenerateWithGoogleTtsAsync(string text, string lang, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                var apiKey = GetGoogleTtsApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return "google_tts_key_missing";
                }

                var client = _httpFactory.CreateClient();
                var endpoint = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={Uri.EscapeDataString(apiKey)}";
                var normalizedLang = NormalizeLang(lang);
                var payload = new
                {
                    input = new { text },
                    voice = new
                    {
                        languageCode = ResolveGoogleLanguageCode(normalizedLang),
                        name = ResolveGoogleVoiceName(normalizedLang),
                        ssmlGender = "NEUTRAL"
                    },
                    audioConfig = new
                    {
                        audioEncoding = "MP3",
                        speakingRate = 1.0,
                        pitch = 0.0
                    }
                };

                var body = JsonSerializer.Serialize(payload);
                using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var providerBody = await response.Content.ReadAsStringAsync();
                    return $"google_tts_http_{(int)response.StatusCode}:{providerBody}";
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("audioContent", out var audioContentElement))
                {
                    return "google_tts_invalid_payload:audioContent_missing";
                }

                var base64Audio = audioContentElement.GetString();
                if (string.IsNullOrWhiteSpace(base64Audio))
                {
                    return "google_tts_invalid_payload:audioContent_empty";
                }

                var bytes = Convert.FromBase64String(base64Audio);
                await System.IO.File.WriteAllBytesAsync(outputPath, bytes);
                return null;
            }
            catch (OperationCanceledException)
            {
                return "google_tts_timeout";
            }
            catch (Exception ex)
            {
                return $"google_tts_exception:{ex.Message}";
            }
        }

        private string ResolveGoogleVoiceName(string lang)
        {
            return NormalizeLang(lang) switch
            {
                "vi" => "vi-VN-Standard-A",
                "en" => "en-US-Standard-C",
                "fr" => "fr-FR-Standard-A",
                "ja" => "ja-JP-Standard-A",
                "ko" => "ko-KR-Standard-A",
                "zh" => "cmn-CN-Standard-A",
                "th" => "th-TH-Standard-A",
                "es" => "es-ES-Standard-A",
                "ru" => "ru-RU-Standard-A",
                "de" => "de-DE-Standard-A",
                "it" => "it-IT-Standard-A",
                "ar" => "ar-XA-Standard-A",
                "hi" => "hi-IN-Standard-A",
                _ => "en-US-Standard-C"
            };
        }

        private string ResolveGoogleLanguageCode(string lang)
        {
            return NormalizeLang(lang) switch
            {
                "vi" => "vi-VN",
                "en" => "en-US",
                "fr" => "fr-FR",
                "ja" => "ja-JP",
                "ko" => "ko-KR",
                "zh" => "cmn-CN",
                "th" => "th-TH",
                "es" => "es-ES",
                "ru" => "ru-RU",
                "de" => "de-DE",
                "it" => "it-IT",
                "ar" => "ar-XA",
                "hi" => "hi-IN",
                _ => "en-US"
            };
        }

        private string? GetGoogleTtsApiKey()
        {
            // Allow several configuration sources for the Google TTS API key.
            // Priority:
            // 1) Explicit request header X-Google-TTS-Key (useful for testing with a temporary key)
            // 2) Query string parameter 'key' (used by public QR callers if provided)
            // 3) App configuration keys (appsettings / environment)
            // 4) Environment variable GOOGLE_TTS_API_KEY

            try
            {
                // Check header first (overrides other sources)
                if (Request?.Headers != null && Request.Headers.TryGetValue("X-Google-TTS-Key", out var headerVals))
                {
                    var headerKey = headerVals.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(headerKey)) return headerKey.Trim();
                }

                // Next allow query string override for callers that include ?key=...
                if (Request?.Query != null && Request.Query.TryGetValue("key", out var queryVals))
                {
                    var q = queryVals.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(q)) return q.Trim();
                }
            }
            catch
            {
                // ignore any issues accessing Request here and fall back to configuration
            }

            return _config["GoogleTts:ApiKey"]
                   ?? _config["Google:TextToSpeech:ApiKey"]
                   ?? _config["GOOGLE_TTS_API_KEY"]
                   ?? Environment.GetEnvironmentVariable("GOOGLE_TTS_API_KEY");
        }

        private static string ComputeMd5(string input)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return Convert.ToHexString(hash).ToLowerInvariant();
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

        private string ResolvePublicAudioUrl(string? value, PoiModel poi, string lang)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var absolute = ToAbsoluteUrl(value);
            if (Uri.TryCreate(absolute, UriKind.Absolute, out var absoluteUri) && IsLoopbackHost(absoluteUri.Host))
            {
                if (!string.IsNullOrWhiteSpace(poi.QrCode)
                    && Uri.TryCreate(poi.QrCode, UriKind.Absolute, out var qrUri)
                    && !IsLoopbackHost(qrUri.Host))
                {
                    var rebuilt = new UriBuilder(absoluteUri)
                    {
                        Scheme = qrUri.Scheme,
                        Host = qrUri.Host,
                        Port = qrUri.IsDefaultPort ? -1 : qrUri.Port
                    };
                    return rebuilt.Uri.ToString();
                }

                var fallback = new UriBuilder(absoluteUri)
                {
                    Scheme = Request.Scheme,
                    Host = Request.Host.Host,
                    Port = Request.Host.Port ?? (Request.IsHttps ? 443 : 80)
                };
                return fallback.Uri.ToString();
            }

            return absolute;
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

        private static bool IsLoopbackHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class PublicQrTrackRequest
    {
        public int PoiId { get; set; }
        public string? Event { get; set; }
        public string? Lang { get; set; }
        public string? Source { get; set; }
        public double? DurationSeconds { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Mode { get; set; }
    }
}
