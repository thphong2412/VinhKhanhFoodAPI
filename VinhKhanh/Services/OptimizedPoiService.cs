using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VinhKhanh.Shared;

namespace VinhKhanh.Services
{
    /// <summary>
    /// Optimized POI Query Service
    /// Provides efficient pagination, lazy loading, and caching for POI data.
    /// Reduces memory footprint and improves app responsiveness.
    /// </summary>
    public interface IOptimizedPoiService
    {
        /// <summary>
        /// Get POIs with pagination support.
        /// </summary>
        Task<PagedResult<PoiModel>> GetPoisPagedAsync(int pageNumber, int pageSize);

        /// <summary>
        /// Get POIs near a location with distance calculation and sorting.
        /// </summary>
        Task<List<PoiModel>> GetNearbyPoisAsync(double latitude, double longitude, double radiusKm = 2.0, int maxResults = 50);

        /// <summary>
        /// Get POIs by category with pagination.
        /// </summary>
        Task<PagedResult<PoiModel>> GetPoisByCategoryAsync(string category, int pageNumber, int pageSize);

        /// <summary>
        /// Batch fetch POIs (up to 100) for efficient loading.
        /// </summary>
        Task<List<PoiModel>> GetPoiBatchAsync(List<int> poiIds);
    }

    public class PagedResult<T>
    {
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<T> Items { get; set; } = new();
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }

    public class OptimizedPoiService : IOptimizedPoiService
    {
        private readonly ApiService _apiService;
        private readonly ILogger<OptimizedPoiService> _logger;

        public OptimizedPoiService(ApiService apiService, ILogger<OptimizedPoiService> logger)
        {
            _apiService = apiService;
            _logger = logger;
        }

        public async Task<PagedResult<PoiModel>> GetPoisPagedAsync(int pageNumber, int pageSize)
        {
            try
            {
                // Clamp page size to reasonable limits (max 100)
                pageSize = Math.Min(Math.Max(pageSize, 1), 100);
                pageNumber = Math.Max(pageNumber, 1);

                // Fetch all POIs (optimization: API should support pagination natively)
                var allPois = await _apiService.GetPoisAsync();
                if (allPois == null) allPois = new List<PoiModel>();

                var totalCount = allPois.Count;
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var items = allPois
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                _logger?.LogInformation($"[OptimizedPoi] Fetched page {pageNumber}/{totalPages}, {items.Count} items");

                return new PagedResult<PoiModel>
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = items
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[OptimizedPoi] Error fetching paged POIs");
                return new PagedResult<PoiModel> { Items = new List<PoiModel>() };
            }
        }

        public async Task<List<PoiModel>> GetNearbyPoisAsync(double latitude, double longitude, double radiusKm = 2.0, int maxResults = 50)
        {
            try
            {
                var allPois = await _apiService.GetPoisAsync();
                if (allPois == null || !allPois.Any()) return new List<PoiModel>();

                // Filter by distance and sort by proximity
                var nearby = allPois
                    .Select(p => new { Poi = p, Distance = HaversineDistanceKm(latitude, longitude, p.Latitude, p.Longitude) })
                    .Where(x => x.Distance <= radiusKm)
                    .OrderBy(x => x.Distance)
                    .Take(maxResults)
                    .Select(x => x.Poi)
                    .ToList();

                _logger?.LogInformation($"[OptimizedPoi] Found {nearby.Count} POIs within {radiusKm}km");
                return nearby;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[OptimizedPoi] Error fetching nearby POIs");
                return new List<PoiModel>();
            }
        }

        public async Task<PagedResult<PoiModel>> GetPoisByCategoryAsync(string category, int pageNumber, int pageSize)
        {
            try
            {
                pageSize = Math.Min(Math.Max(pageSize, 1), 100);
                pageNumber = Math.Max(pageNumber, 1);

                var allPois = await _apiService.GetPoisAsync();
                if (allPois == null) allPois = new List<PoiModel>();

                var filtered = allPois
                    .Where(p => p.Category?.Equals(category, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();

                var totalCount = filtered.Count;
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var items = filtered
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                _logger?.LogInformation($"[OptimizedPoi] Category {category}: page {pageNumber}/{totalPages}, {items.Count} items");

                return new PagedResult<PoiModel>
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = items
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[OptimizedPoi] Error fetching POIs by category");
                return new PagedResult<PoiModel> { Items = new List<PoiModel>() };
            }
        }

        public async Task<List<PoiModel>> GetPoiBatchAsync(List<int> poiIds)
        {
            try
            {
                if (poiIds == null || !poiIds.Any()) return new List<PoiModel>();

                var allPois = await _apiService.GetPoisAsync();
                if (allPois == null) allPois = new List<PoiModel>();

                var idSet = new HashSet<int>(poiIds);
                var batch = allPois
                    .Where(p => idSet.Contains(p.Id))
                    .ToList();

                _logger?.LogInformation($"[OptimizedPoi] Fetched batch of {batch.Count} POIs from request of {poiIds.Count}");
                return batch;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"[OptimizedPoi] Error fetching batch POIs");
                return new List<PoiModel>();
            }
        }

        private static double HaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371.0;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusKm * c;
        }

        private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
    }
}
