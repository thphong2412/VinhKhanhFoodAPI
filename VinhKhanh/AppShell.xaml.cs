using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls; // FIX LỖI: Shell, Application, AppTheme
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics; // FIX LỖI: Color, Colors
using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Font = Microsoft.Maui.Font;

namespace VinhKhanh
{
    public partial class AppShell : Shell
    {
        private readonly Dictionary<string, string> _shellTextCache = new(StringComparer.OrdinalIgnoreCase);

        public AppShell()
        {
            InitializeComponent();

            // Kiểm tra theme hiện tại của hệ thống
            if (Application.Current != null)
            {
                var currentTheme = Application.Current.RequestedTheme;
                // Nếu ông có đặt tên x:Name cho SegmentedControl trong XAML là ThemeSegmentedControl
                // ThemeSegmentedControl.SelectedIndex = currentTheme == AppTheme.Light ? 0 : 1;
            }

            _ = ApplyShellLocalizationAsync();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await ApplyShellLocalizationAsync();
        }

        private async Task ApplyShellLocalizationAsync()
        {
            try
            {
                var lang = NormalizeLanguageCode(Preferences.Default.Get("selected_language", "vi"));
                var map = await LocalizeAsync("Map", lang);
                var dashboard = await LocalizeAsync("Dashboard", lang);
                var restaurants = await LocalizeAsync("Restaurants", lang);
                var settings = await LocalizeAsync("Settings", lang);

                // Traverse Shell hierarchy: ShellItem -> ShellSection -> ShellContent
                foreach (ShellItem shellItem in Items)
                {
                    try
                    {
                        foreach (ShellSection section in shellItem.Items)
                        {
                            foreach (ShellContent content in section.Items)
                            {
                                var route = content.Route?.ToLowerInvariant();
                                switch (route)
                                {
                                    case "map":
                                        content.Title = map;
                                        break;
                                    case "main":
                                        content.Title = dashboard;
                                        break;
                                    case "projects":
                                        content.Title = restaurants;
                                        break;
                                    case "manage":
                                        content.Title = settings;
                                        break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task<string> LocalizeAsync(string source, string languageCode)
        {
            if (string.IsNullOrWhiteSpace(source)) return string.Empty;
            var lang = NormalizeLanguageCode(languageCode);
            if (lang == "en") return source;

            var cacheKey = $"{lang}:{source}";
            if (_shellTextCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl={Uri.EscapeDataString(lang)}&dt=t&q={Uri.EscapeDataString(source)}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return source;
                }

                var body = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                {
                    return source;
                }

                var segments = doc.RootElement[0];
                if (segments.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return source;
                }

                var sb = new System.Text.StringBuilder();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind != System.Text.Json.JsonValueKind.Array || segment.GetArrayLength() == 0) continue;
                    var part = segment[0].GetString();
                    if (!string.IsNullOrWhiteSpace(part)) sb.Append(part);
                }

                var translated = sb.ToString().Trim();
                var value = string.IsNullOrWhiteSpace(translated) ? source : translated;
                _shellTextCache[cacheKey] = value;
                return value;
            }
            catch
            {
                return source;
            }
        }

        private static string NormalizeLanguageCode(string? language)
        {
            var normalized = (language ?? "vi").Trim().ToLowerInvariant();
            if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
            if (normalized.Contains('_')) normalized = normalized.Split('_')[0];
            if (normalized == "vn") return "vi";
            return string.IsNullOrWhiteSpace(normalized) ? "vi" : normalized;
        }

        public static async Task DisplaySnackbarAsync(string message)
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            var snackbarOptions = new SnackbarOptions
            {
                BackgroundColor = Color.FromArgb("#FF3300"),
                TextColor = Colors.White,
                ActionButtonTextColor = Colors.Yellow,
                CornerRadius = new CornerRadius(0),
                Font = Font.SystemFontOfSize(18),
                ActionButtonFont = Font.SystemFontOfSize(14)
            };

            var snackbar = Snackbar.Make(message, visualOptions: snackbarOptions);
            await snackbar.Show(cts.Token);
        }

        public static async Task DisplayToastAsync(string message)
        {
            // Toast không chạy trên Windows, chỉ Android/iOS
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
                return;

            var toast = Toast.Make(message, CommunityToolkit.Maui.Core.ToastDuration.Short, 18);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await toast.Show(cts.Token);
        }

        private void SfSegmentedControl_SelectionChanged(object sender, Syncfusion.Maui.Toolkit.SegmentedControl.SelectionChangedEventArgs e)
        {
            if (Application.Current != null)
            {
                Application.Current.UserAppTheme = e.NewIndex == 0 ? AppTheme.Light : AppTheme.Dark;
            }
        }
    }
}