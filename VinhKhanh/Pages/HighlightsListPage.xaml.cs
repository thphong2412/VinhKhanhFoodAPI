using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using VinhKhanh.Services;
using VinhKhanh.Shared;

namespace VinhKhanh.Pages
{
    public partial class HighlightsListPage : ContentPage
    {
        private readonly string _languageCode;
        private readonly ApiService? _apiService;
        private readonly Func<PoiModel, Task>? _onPoiSelected;

        public HighlightsListPage(
            List<PoiModel> items,
            string languageCode = "vi",
            DatabaseService? dbService = null,
            ApiService? apiService = null,
            Func<PoiModel, Task>? onPoiSelected = null)
        {
            InitializeComponent();
            _languageCode = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant();
            _apiService = apiService;
            _onPoiSelected = onPoiSelected;
            _ = LoadHighlightsAsync(items, languageCode, dbService);
        }

        private async Task LoadHighlightsAsync(List<PoiModel>? items, string languageCode, DatabaseService? dbService)
        {
            if (items == null) return;

            var lang = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant();
            var vm = new System.Collections.ObjectModel.ObservableCollection<VinhKhanh.Shared.HighlightViewModel>();

            foreach (var p in items)
            {
                ContentModel? content = null;
                try
                {
                    if (dbService != null)
                    {
                        content = await dbService.GetContentByPoiIdAsync(p.Id, lang)
                                  ?? await dbService.GetContentByPoiIdAsync(p.Id, "en")
                                  ?? await dbService.GetContentByPoiIdAsync(p.Id, "vi");
                    }
                }
                catch { }

                vm.Add(new VinhKhanh.Shared.HighlightViewModel
                {
                    Poi = p,
                    ImageUrl = ResolveHighlightImageUrl(p.ImageUrl),
                    Name = content?.Title ?? p.Name,
                    Category = p.Category ?? string.Empty,
                    Address = content?.Address ?? string.Empty,
                    RatingDisplay = content != null && content.Rating > 0 ? $"{content.Rating:0.0} ★" : string.Empty,
                    PriceDisplay = content?.GetNormalizedPriceRangeDisplay() ?? string.Empty,
                    ReviewCount = 0,
                    OpeningHours = content?.OpeningHours ?? string.Empty,
                    OpenStatus = BuildOpenStatus(content?.OpeningHours, lang),
                    OpenStatusColorHex = BuildOpenStatus(content?.OpeningHours, lang) == GetOpenLabel(lang) ? "#388E3C" : "#D32F2F"
                });
            }

            MainThread.BeginInvokeOnMainThread(() => CvAllHighlights.ItemsSource = vm);
        }

        private string ResolveHighlightImageUrl(string? rawImage)
        {
            var fallback = "dulich1.jpg";
            if (string.IsNullOrWhiteSpace(rawImage)) return fallback;

            var candidate = rawImage
                .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x?.Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            if (string.IsNullOrWhiteSpace(candidate)) return fallback;

            if (DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual)
            {
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var absOnEmu))
                {
                    return $"http://10.0.2.2:5291{absOnEmu.PathAndQuery}";
                }

                return $"http://10.0.2.2:5291/{candidate.TrimStart('/')}";
            }

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            var preferredBase = Preferences.Default.Get("ApiBaseUrl", string.Empty);
            if (string.IsNullOrWhiteSpace(preferredBase))
            {
                preferredBase = Preferences.Default.Get("VinhKhanh_ApiBaseUrl", string.Empty);
            }

            var authority = "http://localhost:5291";
            if (!string.IsNullOrWhiteSpace(preferredBase) && Uri.TryCreate(preferredBase, UriKind.Absolute, out var configuredBase))
            {
                authority = configuredBase.GetLeftPart(UriPartial.Authority);
            }

            return $"{authority}/{candidate.TrimStart('/')}";
        }

        private async void OnItemTapped(object sender, TappedEventArgs e)
        {
            try
            {
                if (sender is not BindableObject bindable || bindable.BindingContext is not VinhKhanh.Shared.HighlightViewModel selectedVm)
                {
                    return;
                }

                var poi = selectedVm.Poi;
                if (poi == null) return;

                try
                {
                    if (_apiService != null)
                    {
                        poi.Contents = await _apiService.GetContentsByPoiIdAsync(poi.Id);
                    }
                }
                catch { }

                if (_onPoiSelected != null)
                {
                    await _onPoiSelected(poi);
                    await Navigation.PopAsync();
                    return;
                }

                await Navigation.PushAsync(new DetailsPage(poi, _languageCode));
            }
            catch { }
        }

        private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (e.CurrentSelection?.FirstOrDefault() is VinhKhanh.Shared.HighlightViewModel selectedVm)
                {
                    var poi = selectedVm.Poi;
                    if (poi == null) return;

                    try
                    {
                        if (_apiService != null)
                        {
                            poi.Contents = await _apiService.GetContentsByPoiIdAsync(poi.Id);
                        }
                    }
                    catch { }

                    if (_onPoiSelected != null)
                    {
                        await _onPoiSelected(poi);
                        await Navigation.PopAsync();
                        return;
                    }

                    await Navigation.PushAsync(new DetailsPage(poi, _languageCode));
                }
            }
            catch { }
            finally
            {
                if (sender is CollectionView cv)
                {
                    cv.SelectedItem = null;
                }
            }
        }

        private static string GetOpenLabel(string languageCode)
        {
            var lang = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant();
            return lang == "vi" ? "Mở cửa" : "Open";
        }

        private static string GetClosedLabel(string languageCode)
        {
            var lang = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant();
            return lang == "vi" ? "Đóng cửa" : "Closed";
        }

        private static string BuildOpenStatus(string? openingHours, string languageCode)
        {
            if (string.IsNullOrWhiteSpace(openingHours)) return string.Empty;

            var parts = openingHours.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) return string.Empty;

            if (!TimeSpan.TryParse(parts[0], out var start) || !TimeSpan.TryParse(parts[1], out var end))
            {
                return string.Empty;
            }

            var now = DateTime.Now.TimeOfDay;
            var isOpen = start <= end ? (now >= start && now <= end) : (now >= start || now <= end);
            return isOpen ? GetOpenLabel(languageCode) : GetClosedLabel(languageCode);
        }
    }
}
