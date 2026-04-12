using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace VinhKhanh.Analytics
{
    // Background service to process queued audio items (TTS generation / placeholder)
    public class AudioProcessingService : BackgroundService
    {
        private readonly ILogger<AudioProcessingService> _logger;
        private readonly IServiceProvider _services;

        public AudioProcessingService(ILogger<AudioProcessingService> logger, IServiceProvider services)
        {
            _logger = logger;
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AudioProcessingService started");

            // Use HTTP API to fetch pending audio and trigger processing in API (keeps worker decoupled)
            using var http = new HttpClient { BaseAddress = new Uri("https://localhost:7174/") };
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pending = await http.GetFromJsonAsync<List<VinhKhanh.Shared.AudioModel>>("api/audio/pending", stoppingToken);
                    if (pending != null && pending.Any())
                    {
                        foreach (var item in pending.Take(5))
                        {
                            try
                            {
                                _logger.LogInformation("Requesting API to process audio {Id}", item.Id);
                                var res = await http.PostAsync($"api/audio/process/{item.Id}", null, stoppingToken);
                                if (!res.IsSuccessStatusCode)
                                {
                                    _logger.LogWarning("API process failed for {Id}: {Status}", item.Id, res.StatusCode);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to request process for audio {Id}", item.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AudioProcessingService error");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
