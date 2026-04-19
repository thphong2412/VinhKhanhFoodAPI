using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VinhKhanh.Shared;
using Microsoft.Maui.Storage;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Service to handle real-time POI, Audio, and Content sync via SignalR
    /// </summary>
    public class SignalRSyncService
    {
        private HubConnection _hubConnection;
        private bool _isConnected = false;

        // POI Events
        public event Func<PoiModel, Task> OnPoiAdded;
        public event Func<PoiModel, Task> OnPoiUpdated;
        public event Func<int, Task> OnPoiDeleted;

        // Audio Events
        public event Func<AudioModel, Task> OnAudioUploaded;
        public event Func<int, int, Task> OnAudioDeleted; // (audioId, poiId)
        public event Func<AudioModel, Task> OnAudioProcessed; // TTS processed

        // Content Events
        public event Func<ContentModel, Task> OnContentCreated;
        public event Func<ContentModel, Task> OnContentUpdated;
        public event Func<int, Task> OnContentDeleted;

        // Connection Events
        public event Func<Task> OnConnected;
        public event Func<Task> OnDisconnected;
        public event Func<Task> OnRequestFullSync;

        public SignalRSyncService()
        {
        }

        /// <summary>
        /// Connect to SignalR hub (auto-detect URL based on platform)
        /// </summary>
        public async Task ConnectAsync()
        {
            if (Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.Android
                && Microsoft.Maui.Devices.DeviceInfo.DeviceType == Microsoft.Maui.Devices.DeviceType.Virtual)
            {
                await ConnectForDeviceAsync();
                return;
            }

            // Cho phép override endpoint cho máy thật qua Preferences (ApiBaseUrl/VinhKhanh_ApiBaseUrl)
            var preferredBaseUrl = Preferences.Get("ApiBaseUrl", string.Empty);
            if (string.IsNullOrWhiteSpace(preferredBaseUrl))
            {
                preferredBaseUrl = Preferences.Get("VinhKhanh_ApiBaseUrl", string.Empty);
            }

            string hubUrl;
            if (!string.IsNullOrWhiteSpace(preferredBaseUrl) && Uri.TryCreate(preferredBaseUrl, UriKind.Absolute, out var preferredUri))
            {
                var authority = preferredUri.GetLeftPart(UriPartial.Authority);
                hubUrl = $"{authority}/sync";
            }
            else
            {
                // Auto-detect hub URL based on platform (fallback)
                hubUrl = Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.Android
                    ? $"http://{GetPreferredAndroidHost()}:5291/sync"
                    : "http://localhost:5291/sync";
            }

            await ConnectAsync(hubUrl);
        }

        /// <summary>
        /// Connect to SignalR hub with custom URL
        /// </summary>
        public async Task ConnectAsync(string hubUrl)
        {
            if (_isConnected) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[SignalR] Connecting to {hubUrl}");

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.HttpMessageHandlerFactory = inner =>
                        {
                            var handler = new HttpClientHandler();
                            // ⚠️ For development only! Remove in production
                            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                            return handler;
                        };
                    })
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10) })
                    .Build();

                // ========== POI Events ==========
                _hubConnection.On<PoiModel>("PoiAdded", async (poi) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] POI Added: {poi.Name}");
                    await (OnPoiAdded?.Invoke(poi) ?? Task.CompletedTask);
                });

                // Some endpoints broadcast "PoiCreated" (e.g. admin approve/publish flow).
                // Treat it as "added" to keep client behavior consistent.
                _hubConnection.On<PoiModel>("PoiCreated", async (poi) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] POI Created: {poi?.Name}");
                    await (OnPoiAdded?.Invoke(poi) ?? Task.CompletedTask);
                });

                _hubConnection.On<PoiModel>("PoiUpdated", async (poi) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] POI Updated: {poi.Name}");
                    await (OnPoiUpdated?.Invoke(poi) ?? Task.CompletedTask);
                });

                _hubConnection.On<int>("PoiDeleted", async (poiId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] POI Deleted: {poiId}");
                    await (OnPoiDeleted?.Invoke(poiId) ?? Task.CompletedTask);
                });

                // ========== Audio Events ==========
                _hubConnection.On<AudioModel>("AudioUploaded", async (audio) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] Audio Uploaded: POI {audio.PoiId} ({audio.LanguageCode})");
                    await (OnAudioUploaded?.Invoke(audio) ?? Task.CompletedTask);
                });

                _hubConnection.On("AudioDeleted", async (int audioId, int poiId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] Audio Deleted: {audioId} from POI {poiId}");
                    await (OnAudioDeleted?.Invoke(audioId, poiId) ?? Task.CompletedTask);
                });

                // Some hub methods may broadcast AudioDeleted as an object: { id, poiId }
                _hubConnection.On<object>("AudioDeleted", async (payload) =>
                {
                    try
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(payload);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var parsedId) ? parsedId : 0;
                        var poiId = root.TryGetProperty("poiId", out var poiEl) && poiEl.TryGetInt32(out var parsedPoiId) ? parsedPoiId : 0;
                        if (id > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SignalR] Audio Deleted(payload): {id} from POI {poiId}");
                            await (OnAudioDeleted?.Invoke(id, poiId) ?? Task.CompletedTask);
                        }
                    }
                    catch { }
                });

                _hubConnection.On<AudioModel>("AudioProcessed", async (audio) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] TTS Processed: {audio.Id}");
                    await (OnAudioProcessed?.Invoke(audio) ?? Task.CompletedTask);
                });

                // ========== Content Events ==========
                _hubConnection.On<ContentModel>("ContentCreated", async (content) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] Content Created: {content.Title} ({content.LanguageCode})");
                    await (OnContentCreated?.Invoke(content) ?? Task.CompletedTask);
                });

                _hubConnection.On<ContentModel>("ContentUpdated", async (content) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] Content Updated: {content.Title} ({content.LanguageCode})");
                    await (OnContentUpdated?.Invoke(content) ?? Task.CompletedTask);
                });

                _hubConnection.On<int>("ContentDeleted", async (contentId) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[SignalR] Content Deleted: {contentId}");
                    await (OnContentDeleted?.Invoke(contentId) ?? Task.CompletedTask);
                });

                // Server sends RequestFullPoiSync with a payload (timestamp). Accept any payload shape.
                _hubConnection.On<object>("RequestFullPoiSync", async (_) =>
                {
                    System.Diagnostics.Debug.WriteLine("[SignalR] RequestFullPoiSync received");
                    await (OnRequestFullSync?.Invoke() ?? Task.CompletedTask);
                });

                // ========== Connection Events ==========
                _hubConnection.Closed += async (error) =>
                {
                    _isConnected = false;
                    System.Diagnostics.Debug.WriteLine($"[SignalR] Disconnected: {error?.Message}");
                    await (OnDisconnected?.Invoke() ?? Task.CompletedTask);
                };

                _hubConnection.Reconnected += async (connectionId) =>
                {
                    _isConnected = true;
                    System.Diagnostics.Debug.WriteLine("[SignalR] Reconnected");
                    await (OnConnected?.Invoke() ?? Task.CompletedTask);
                };

                // Bound connection time to avoid long startup stalls on emulator.
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(4));
                await _hubConnection.StartAsync(cts.Token);
                _isConnected = true;
                System.Diagnostics.Debug.WriteLine("✅ SignalR connected");
                await (OnConnected?.Invoke() ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                System.Diagnostics.Debug.WriteLine($"❌ SignalR connection failed: {ex.Message}");
            }
        }

        public async Task ConnectForDeviceAsync()
        {
            if (Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.Android
                && Microsoft.Maui.Devices.DeviceInfo.DeviceType == Microsoft.Maui.Devices.DeviceType.Virtual)
            {
                var candidates = new[]
                {
                    "http://10.0.2.2:5291/sync",
                    "http://localhost:5291/sync"
                };

                foreach (var candidate in candidates)
                {
                    await ConnectAsync(candidate);
                    if (IsConnected) return;
                }

                return;
            }

            // Android real device / other platforms: try multiple hubs to avoid sticking to wrong stored URL
            var preferredBaseUrl = Preferences.Get("ApiBaseUrl", string.Empty);
            if (string.IsNullOrWhiteSpace(preferredBaseUrl))
            {
                preferredBaseUrl = Preferences.Get("VinhKhanh_ApiBaseUrl", string.Empty);
            }

            var candidatesForDevice = new List<string>();

            if (!string.IsNullOrWhiteSpace(preferredBaseUrl) && Uri.TryCreate(preferredBaseUrl, UriKind.Absolute, out var preferredUri))
            {
                var preferredAuthority = preferredUri.GetLeftPart(UriPartial.Authority);
                candidatesForDevice.Add($"{preferredAuthority}/sync");

                // Also try same host on API port 5291 in case preference points to Admin/Owner portal port
                candidatesForDevice.Add($"{preferredUri.Scheme}://{preferredUri.Host}:5291/sync");
            }

            candidatesForDevice.Add("http://192.168.1.7:5291/sync");
            candidatesForDevice.Add("http://localhost:5291/sync");
            candidatesForDevice.Add("http://10.0.2.2:5291/sync");

            foreach (var candidate in candidatesForDevice.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await ConnectAsync(candidate);
                if (IsConnected) return;
            }
        }

        /// <summary>
        /// Disconnect from SignalR
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_hubConnection != null)
            {
                try
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Disconnect error: {ex.Message}");
                }
            }
            _isConnected = false;
        }

        /// <summary>
        /// Check connection status
        /// </summary>
        public bool IsConnected => _isConnected && _hubConnection?.State == HubConnectionState.Connected;

        private static string GetPreferredAndroidHost()
        {
            if (Microsoft.Maui.Devices.DeviceInfo.DeviceType == Microsoft.Maui.Devices.DeviceType.Virtual)
            {
                return "10.0.2.2";
            }

            return "192.168.1.7";
        }
    }
}
