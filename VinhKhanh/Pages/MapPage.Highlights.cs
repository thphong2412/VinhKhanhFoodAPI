using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class MapPage
    {
        // Allow dragging highlights panel up to view more
        public void OnHighlightsPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            try
            {
                if (HighlightsPanel == null || _isHighlightsAnimating) return;
                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                        _highlightsStartTranslationY = HighlightsPanel.TranslationY;
                        break;
                    case GestureStatus.Running:
                        var rawY = _highlightsStartTranslationY + e.TotalY;
                        var minY = -(HighlightsExpandedHeight - HighlightsCollapsedHeight);
                        var maxY = 0d;
                        HighlightsPanel.TranslationY = Math.Clamp(rawY, minY, maxY);
                        break;
                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        var threshold = 12d;
                        var currentY = HighlightsPanel.TranslationY;
                        var midpoint = -(HighlightsExpandedHeight - HighlightsCollapsedHeight) / 2d;
                        var shouldExpand = e.TotalY <= -threshold
                            ? true
                            : e.TotalY >= threshold
                                ? false
                                : currentY <= midpoint;

                        HighlightsPanel.TranslationY = 0;
                        SetHighlightsExpandedState(shouldExpand);
                        break;
                }
            }
            catch { }
        }

        private void OnHighlightsPanelTapped(object sender, EventArgs e)
        {
            try
            {
                if (!_isHighlightsExpanded)
                {
                    SetHighlightsExpandedState(true);
                }
            }
            catch { }
        }

        private void OnScrollHighlightsUpClicked(object sender, EventArgs e)
        {
            try
            {
                _ = ScrollHighlightsBy(-1);
            }
            catch { }
        }

        private void OnScrollHighlightsDownClicked(object sender, EventArgs e)
        {
            try
            {
                _ = ScrollHighlightsBy(1);
            }
            catch { }
        }

        private async Task ScrollHighlightsBy(int delta)
        {
            try
            {
                if (CvHighlights?.ItemsSource is not IEnumerable<VinhKhanh.Shared.HighlightViewModel> items) return;
                var list = items.ToList();
                if (!list.Any()) return;

                if (!_isHighlightsExpanded)
                {
                    await AnimateHighlightsExpandedStateAsync(true);
                }

                _highlightScrollIndex = Math.Clamp(_highlightScrollIndex + delta, 0, list.Count - 1);
                CvHighlights.ScrollTo(_highlightScrollIndex, position: ScrollToPosition.Center, animate: true);
            }
            catch { }
        }

        private void SetHighlightsExpandedState(bool expanded)
        {
            _ = AnimateHighlightsExpandedStateAsync(expanded);
        }

        private async Task AnimateHighlightsExpandedStateAsync(bool expanded)
        {
            await _highlightsAnimationLock.WaitAsync();
            try
            {
                if (HighlightsPanel == null) return;
                var targetPanelHeight = expanded ? HighlightsExpandedHeight : HighlightsCollapsedHeight;
                var targetListHeight = expanded ? 360 : 0;

                if (_isHighlightsExpanded == expanded
                    && Math.Abs((HighlightsPanel.HeightRequest <= 0 ? HighlightsCollapsedHeight : HighlightsPanel.HeightRequest) - targetPanelHeight) < 0.5)
                {
                    if (CvHighlights != null)
                    {
                        CvHighlights.IsVisible = expanded;
                        CvHighlights.HeightRequest = targetListHeight;
                    }

                    if (BtnViewAllHighlights != null)
                    {
                        BtnViewAllHighlights.IsVisible = expanded;
                    }

                    if (BtnToggleHighlights != null)
                    {
                        BtnToggleHighlights.Text = expanded ? "-" : "+";
                    }

                    return;
                }

                _isHighlightsAnimating = true;

                this.AbortAnimation("HighlightsPanelHeightAnim");
                this.AbortAnimation("HighlightsListHeightAnim");

                HighlightsPanel.TranslationY = 0;
                HighlightsPanel.HeightRequest = targetPanelHeight;

                if (CvHighlights != null)
                {
                    if (expanded)
                    {
                        CvHighlights.IsVisible = true;
                        CvHighlights.HeightRequest = targetListHeight;
                        await CvHighlights.FadeTo(1, 140, Easing.CubicOut);
                    }
                    else
                    {
                        await CvHighlights.FadeTo(0, 100, Easing.CubicIn);
                        CvHighlights.HeightRequest = 0;
                        CvHighlights.IsVisible = false;
                    }
                }

                if (BtnViewAllHighlights != null)
                {
                    if (expanded)
                    {
                        BtnViewAllHighlights.IsVisible = true;
                        await BtnViewAllHighlights.FadeTo(1, 140, Easing.CubicOut);
                    }
                    else
                    {
                        await BtnViewAllHighlights.FadeTo(0, 100, Easing.CubicIn);
                        BtnViewAllHighlights.IsVisible = false;
                    }
                }

                if (HighlightsLoadingIndicator != null && !expanded)
                {
                    HighlightsLoadingIndicator.IsRunning = false;
                    HighlightsLoadingIndicator.IsVisible = false;
                }

                _isHighlightsExpanded = expanded;

                if (BtnToggleHighlights != null)
                {
                    BtnToggleHighlights.Text = expanded ? "-" : "+";
                }
            }
            catch { }
            finally
            {
                _isHighlightsAnimating = false;
                _highlightsAnimationLock.Release();
            }
        }

        private async Task RenderHighlightsAsync(IEnumerable<PoiModel> sourcePois, bool lightweight = false)
        {
            try
            {
                var normalizedSource = (sourcePois ?? Enumerable.Empty<PoiModel>())
                    .Where(p => p != null)
                    .DistinctBy(p => p.Id)
                    .OrderByDescending(p => p.Priority)
                    .ToList();

                if (!normalizedSource.Any())
                {
                    await EnsurePoiDataReadyAsync();
                    normalizedSource = (_pois ?? new List<PoiModel>())
                        .Where(p => p != null)
                        .DistinctBy(p => p.Id)
                        .OrderByDescending(p => p.Priority)
                        .Take(12)
                        .ToList();
                }

                var vmColl = new System.Collections.ObjectModel.ObservableCollection<VinhKhanh.Shared.HighlightViewModel>();
                var pois = normalizedSource;
                var ui = await BuildDynamicUiTextAsync(_currentLanguage);

                if (!lightweight)
                {
                    await EnsureLiveStatsCacheAsync();

                    pois = pois
                        .OrderByDescending(p => _liveStatsByPoiId.TryGetValue(p.Id, out var s) && s.IsHot)
                        .ThenByDescending(p => _liveStatsByPoiId.TryGetValue(p.Id, out var s) ? s.ActiveUsers : 0)
                        .ThenByDescending(p => _liveStatsByPoiId.TryGetValue(p.Id, out var s) ? s.QrScanCount : 0)
                        .ThenByDescending(p => p.Priority)
                        .ToList();
                }

                var contentMap = new Dictionary<int, ContentModel?>();
                var preferredLanguage = NormalizeLanguageCode(_currentLanguage);
                var languageChain = GetLanguageFallbackChain(preferredLanguage, includeVi: true).ToList();
                var allContents = await _dbService.GetAllContentsAsync() ?? new List<ContentModel>();
                var contentGroups = allContents
                    .Where(c => c != null)
                    .GroupBy(c => c.PoiId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var poi in pois)
                {
                    if (!contentGroups.TryGetValue(poi.Id, out var candidates) || candidates == null || !candidates.Any())
                    {
                        contentMap[poi.Id] = null;
                        continue;
                    }

                    ContentModel? selected = null;
                    foreach (var lang in languageChain)
                    {
                        selected = candidates
                            .Where(c => NormalizeLanguageCode(c.LanguageCode) == lang)
                            .OrderByDescending(ComputeContentQualityScore)
                            .ThenByDescending(c => c.Id)
                            .FirstOrDefault();
                        if (selected != null) break;
                    }

                    selected ??= candidates
                        .OrderByDescending(ComputeContentQualityScore)
                        .ThenByDescending(c => c.Id)
                        .FirstOrDefault();

                    contentMap[poi.Id] = selected;
                }

                if (!lightweight)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var needHydrate = pois.Where(p =>
                                !contentMap.TryGetValue(p.Id, out var c) || !HasMeaningfulContent(c))
                                .Take(4)
                                .ToList();

                            foreach (var poi in needHydrate)
                            {
                                try { await HydrateContentsFromApiAsync(poi.Id); } catch { }
                            }
                        }

                        catch { }
                    });
                }

                var localizedCategoryCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var h in pois)
                {
                    contentMap.TryGetValue(h.Id, out var content);
                    var resolvedImageUrl = ResolveHighlightImageUrl(h.ImageUrl);
                    var cachedImage = lightweight ? resolvedImageUrl : await ResolveHighlightImageSourceAsync(resolvedImageUrl, h.Id);
                    var categoryKey = h.Category ?? string.Empty;
                    if (!localizedCategoryCache.TryGetValue(categoryKey, out var localizedCategory))
                    {
                        localizedCategory = await LocalizeFreeTextAsync(h.Category, _currentLanguage);
                        localizedCategoryCache[categoryKey] = localizedCategory;
                    }
                    var openStatus = string.Empty;
                    var openColorHex = "#9E9E9E";
                    if (content != null && !string.IsNullOrEmpty(content.OpeningHours))
                    {
                        var parts = content.OpeningHours.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                        if (parts.Length == 2 && TimeSpan.TryParse(parts[0], out var s) && TimeSpan.TryParse(parts[1], out var e))
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var isOpen = s <= e ? now >= s && now <= e : now >= s || now <= e;
                            openStatus = isOpen ? ui["open_now"] : ui["closed"];
                            openColorHex = isOpen ? "#388E3C" : "#D32F2F";
                        }
                    }

                    vmColl.Add(new VinhKhanh.Shared.HighlightViewModel
                    {
                        Poi = h,
                        ImageUrl = cachedImage,
                        Name = content?.Title ?? h.Name,
                        Category = localizedCategory,
                        Address = content?.Address ?? string.Empty,
                        RatingDisplay = content != null ? (content.Rating > 0 ? string.Format("{0:0.0} ★", content.Rating) : ui["no_rating"]) : ui["no_rating"],
                        PriceDisplay = content?.GetNormalizedPriceRangeDisplay() ?? string.Empty,
                        ReviewCount = content != null && content.Rating > 0 ? Math.Max(1, (int)Math.Round(content.Rating * 20)) : 0,
                        OpeningHours = content?.OpeningHours ?? string.Empty,
                        OpenStatus = openStatus,
                        OpenStatusColorHex = openColorHex,
                        BadgeText = (_liveStatsByPoiId.TryGetValue(h.Id, out var live) && live.IsHot)
                            ? "HOT"
                            : (content?.Rating > 0 ? $"★ {content.Rating:0.0}" : ui["new_badge"]),
                        PopularHint = (_liveStatsByPoiId.TryGetValue(h.Id, out var liveHint) && liveHint.IsHot)
                            ? ui["popular_hint_hot"]
                            : string.Empty,
                        IsHot = _liveStatsByPoiId.TryGetValue(h.Id, out var isHotLive) && isHotLive.IsHot
                    });
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    CvHighlights.ItemsSource = vmColl;
                    try { CvHighlights.SelectedItem = null; } catch { }
                    HighlightsPanel.IsVisible = true;
                    if (vmColl.Any())
                    {
                        SetHighlightsExpandedState(_isHighlightsExpanded);
                    }
                });

                if (!lightweight)
                {
                    _ = PrimeHighlightImagesCacheAsync(pois);
                }
            }
            catch { }
        }

        private async Task EnsureLiveStatsCacheAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (_liveStatsByPoiId.Count > 0 && (now - _lastLiveStatsFetchUtc).TotalSeconds < 25)
                {
                    return;
                }

                if (_isPageInitializing)
                {
                    return;
                }

                await EnsureApiBaseReadyAsync();
                var stats = await _apiService.GetPoiLiveStatsAsync(_lastLocation?.Latitude, _lastLocation?.Longitude, 100) ?? new List<PoiLiveStatsDto>();
                _liveStatsByPoiId = stats
                    .Where(x => x != null && x.PoiId > 0)
                    .GroupBy(x => x.PoiId)
                    .ToDictionary(g => g.Key, g => g.First());
                _lastLiveStatsFetchUtc = now;
            }
            catch { }
        }

        private async Task PrimeHighlightImagesCacheAsync(IEnumerable<PoiModel> pois)
        {
            try
            {
                var list = (pois ?? Enumerable.Empty<PoiModel>()).Where(p => p != null).Take(12).ToList();
                var maxParallel = Microsoft.Maui.Devices.DeviceInfo.DeviceType == Microsoft.Maui.Devices.DeviceType.Virtual ? 1 : 2;
                // Prefetch images in parallel but limit concurrency to reduce network/IO contention
                var sem = new SemaphoreSlim(maxParallel);
                var tasks = new List<Task>();
                foreach (var poi in list)
                {
                    await sem.WaitAsync();
                    var p = poi;
                    tasks.Add(Task.Run(async () =>
                    {
                        try { await ResolveHighlightImageSourceAsync(p.ImageUrl, p.Id); } catch { }
                        finally { try { sem.Release(); } catch { } }
                    }));
                }
                try { await Task.WhenAll(tasks); } catch { }
                await Task.CompletedTask;
            }
            catch { }
        }

        private async Task<string> ResolveHighlightImageSourceAsync(string? source, int poiId)
        {
            if (string.IsNullOrWhiteSpace(source)) return source ?? string.Empty;

            var raw = ResolveHighlightImageUrl(source);
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                return raw;
            }

            // Bypass local cache for uploaded POI images to reflect Admin changes immediately.
            var isUploadedImage = uri.AbsolutePath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase);

            if (!isUploadedImage && _highlightImageCache.TryGetValue(raw, out var existing) && !string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
            {
                return existing;
            }

            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "highlight-img");
            Directory.CreateDirectory(cacheDir);

            var ext = Path.GetExtension(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".jpg";
            var key = ComputeStableHash(raw);
            var localPath = Path.Combine(cacheDir, $"poi_{poiId}_{key}{ext}");

            if (!isUploadedImage && File.Exists(localPath))
            {
                _highlightImageCache[raw] = localPath;
                return localPath;
            }

            try
            {
                await _highlightImageDownloadGate.WaitAsync();

                if (!isUploadedImage && File.Exists(localPath))
                {
                    _highlightImageCache[raw] = localPath;
                    return localPath;
                }

                var bytes = await _highlightImageHttpClient.GetByteArrayAsync(uri);
                if (bytes != null && bytes.Length > 0)
                {
                    await File.WriteAllBytesAsync(localPath, bytes);
                    _highlightImageCache[raw] = localPath;
                    return localPath;
                }
            }
            catch { }
            finally
            {
                try { _highlightImageDownloadGate.Release(); } catch { }
            }

            return raw;
        }

        private string ResolveHighlightImageUrl(string? source)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(source)) return string.Empty;

                var raw = source
                    .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x?.Trim())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? source.Trim();

                return ToAbsoluteApiUrl(raw);
            }
            catch
            {
                return source ?? string.Empty;
            }
        }

        private static string ComputeStableHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
        }
    }
}
