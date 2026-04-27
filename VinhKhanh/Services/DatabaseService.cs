using SQLite;
using VinhKhanh.Shared;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace VinhKhanh.Services
{
    public class DatabaseService
    {
        private readonly SQLiteAsyncConnection _database;
        private readonly Task _initTask;

        public DatabaseService()
        {
            // Kết nối đến file database SQLite trong thư mục cục bộ của App
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "VinhKhanh.db3");
            _database = new SQLiteAsyncConnection(dbPath);

            _initTask = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await _database.CreateTableAsync<PoiModel>();
            await _database.CreateTableAsync<ContentModel>();
            await _database.CreateTableAsync<VinhKhanh.Shared.AudioModel>();
        }

        private Task EnsureInitializedAsync() => _initTask;

        // --- 1. LẤY DANH SÁCH ĐỊA ĐIỂM ---
        public async Task<List<PoiModel>> GetPoisAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<PoiModel>().ToListAsync();
        }

        // --- 2. LƯU ĐỊA ĐIỂM (QUÁN ĂN, TRẠM XE, DU LỊCH) ---
        public async Task<int> SavePoiAsync(PoiModel poi)
        {
            await EnsureInitializedAsync();
            if (poi == null) return 0;

            // Upsert theo Id để đảm bảo đồng bộ POI từ API luôn đầy đủ
            if (poi.Id != 0)
            {
                var existing = await _database.Table<PoiModel>()
                    .Where(p => p.Id == poi.Id)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    return await _database.UpdateAsync(poi);
                }

                return await _database.InsertAsync(poi);
            }

            return await _database.InsertAsync(poi);
        }

        // --- 3. LƯU NỘI DUNG THUYẾT MINH ---
        public async Task<int> SaveContentAsync(ContentModel content)
        {
            await EnsureInitializedAsync();
            if (content == null) return 0;

            content.LanguageCode = NormalizeLanguageCode(content.LanguageCode);

            if (content.Id > 0)
            {
                var existingById = await _database.Table<ContentModel>()
                    .Where(c => c.Id == content.Id)
                    .FirstOrDefaultAsync();

                if (existingById != null)
                {
                    return await _database.UpdateAsync(content);
                }

                var existingByPoiAndLanguage = await _database.Table<ContentModel>()
                    .Where(c => c.PoiId == content.PoiId && c.LanguageCode == content.LanguageCode)
                    .FirstOrDefaultAsync();

                if (existingByPoiAndLanguage != null)
                {
                    await _database.DeleteAsync(existingByPoiAndLanguage);
                }

                return await _database.InsertAsync(content);
            }

            // fallback cho dữ liệu cũ không có server Id
            var existing = await _database.Table<ContentModel>()
                .Where(c => c.PoiId == content.PoiId && c.LanguageCode == content.LanguageCode)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                content.Id = existing.Id;
                return await _database.UpdateAsync(content);
            }

            return await _database.InsertAsync(content);
        }

        public async Task<ContentModel?> GetContentByIdAsync(int contentId)
        {
            await EnsureInitializedAsync();
            if (contentId <= 0) return null;

            return await _database.Table<ContentModel>()
                .Where(c => c.Id == contentId)
                .FirstOrDefaultAsync();
        }

        // --- 4. LẤY NỘI DUNG THEO ID VÀ NGÔN NGỮ (Dùng để đọc thuyết minh) ---
        public async Task<ContentModel> GetContentByPoiIdAsync(int poiId, string lang)
        {
            await EnsureInitializedAsync();
            var normalizedLang = NormalizeLanguageCode(lang);

            var exactList = await _database.Table<ContentModel>()
                                  .Where(c => c.PoiId == poiId && c.LanguageCode == normalizedLang)
                                  .ToListAsync();
            var exact = exactList
                .OrderByDescending(c => ComputeContentQualityScore(c))
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();
            if (exact != null) return exact;

            // fallback cho dữ liệu cũ chưa normalize (ví dụ: en-US)
            var fallbackList = await _database.Table<ContentModel>()
                                  .Where(c => c.PoiId == poiId && c.LanguageCode.StartsWith(normalizedLang))
                                  .ToListAsync();

            var fallback = fallbackList
                .OrderByDescending(c => ComputeContentQualityScore(c))
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();

            if (fallback != null) return fallback;

            return await _database.Table<ContentModel>()
                .Where(c => c.PoiId == poiId && c.LanguageCode == "en")
                .OrderByDescending(c => ComputeContentQualityScore(c))
                .ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<ContentModel>> GetContentsByPoiIdAsync(int poiId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<ContentModel>()
                                  .Where(c => c.PoiId == poiId)
                                  .ToListAsync();
        }

        public async Task<List<ContentModel>> GetAllContentsAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<ContentModel>().ToListAsync();
        }

        // --- Audio helpers ---
        public async Task<int> SaveAudioAsync(VinhKhanh.Shared.AudioModel audio)
        {
            await EnsureInitializedAsync();
            if (audio == null) return 0;

            audio.LanguageCode = NormalizeLanguageCode(audio.LanguageCode);

            if (audio.Id > 0)
            {
                var existingById = await _database.Table<VinhKhanh.Shared.AudioModel>()
                    .Where(a => a.Id == audio.Id)
                    .FirstOrDefaultAsync();

                if (existingById != null)
                {
                    return await _database.UpdateAsync(audio);
                }

                return await _database.InsertAsync(audio);
            }

            var existingByPoiAndLangAndUrl = await _database.Table<VinhKhanh.Shared.AudioModel>()
                .Where(a => a.PoiId == audio.PoiId
                            && a.LanguageCode == audio.LanguageCode
                            && a.Url == audio.Url
                            && a.IsTts == audio.IsTts)
                .FirstOrDefaultAsync();

            if (existingByPoiAndLangAndUrl != null)
            {
                audio.Id = existingByPoiAndLangAndUrl.Id;
                return await _database.UpdateAsync(audio);
            }

            return await _database.InsertAsync(audio);
        }

        public async Task<VinhKhanh.Shared.AudioModel> GetAudioByPoiAndLangAsync(int poiId, string lang)
        {
            await EnsureInitializedAsync();
            var normalizedLang = NormalizeLanguageCode(lang);

            var exactList = await _database.Table<VinhKhanh.Shared.AudioModel>()
                .Where(a => a.PoiId == poiId && a.LanguageCode == normalizedLang)
                .ToListAsync();
            var exact = exactList
                .OrderByDescending(a => a.IsProcessed)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.Id)
                .FirstOrDefault();
            if (exact != null) return exact;

            var fallbackList = await _database.Table<VinhKhanh.Shared.AudioModel>()
                .Where(a => a.PoiId == poiId && a.LanguageCode.StartsWith(normalizedLang))
                .ToListAsync();

            var fallback = fallbackList
                .OrderByDescending(a => a.IsProcessed)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.Id)
                .FirstOrDefault();

            if (fallback != null) return fallback;

            return await _database.Table<VinhKhanh.Shared.AudioModel>()
                .Where(a => a.PoiId == poiId && a.LanguageCode == "en")
                .OrderByDescending(a => a.IsProcessed)
                .ThenByDescending(a => a.CreatedAtUtc)
                .ThenByDescending(a => a.Id)
                .FirstOrDefaultAsync();
        }

        private static int ComputeContentQualityScore(ContentModel content)
        {
            if (content == null) return 0;

            var score = 0;
            if (!string.IsNullOrWhiteSpace(content.Title)) score += 4;
            if (!string.IsNullOrWhiteSpace(content.Description)) score += 6;
            if (!string.IsNullOrWhiteSpace(content.Subtitle)) score += 2;
            if (!string.IsNullOrWhiteSpace(content.Address)) score += 3;
            if (!string.IsNullOrWhiteSpace(content.PhoneNumber)) score += 2;
            if (!string.IsNullOrWhiteSpace(content.OpeningHours)) score += 2;
            if (content.Rating > 0) score += 1;
            return score;
        }

        private static string NormalizeLanguageCode(string? languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) return "vi";
            var normalized = languageCode.Trim().ToLowerInvariant();
            if (normalized.Contains('-')) normalized = normalized.Split('-')[0];
            if (normalized.Contains('_')) normalized = normalized.Split('_')[0];
            return normalized;
        }

        public async Task<System.Collections.Generic.List<VinhKhanh.Shared.AudioModel>> GetAudiosByPoiAsync(int poiId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<VinhKhanh.Shared.AudioModel>()
                .Where(a => a.PoiId == poiId)
                .ToListAsync();
        }

        public async Task<System.Collections.Generic.List<VinhKhanh.Shared.AudioModel>> GetAllAudiosAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<VinhKhanh.Shared.AudioModel>().ToListAsync();
        }
        // Thêm vào trong class DatabaseService
        public async Task ClearAllPoisAsync()
        {
            await EnsureInitializedAsync();
            await _database.DeleteAllAsync<PoiModel>();
            await _database.DeleteAllAsync<ContentModel>();
            await _database.DeleteAllAsync<VinhKhanh.Shared.AudioModel>();
        }

        public async Task<int> DeletePoiByIdAsync(int poiId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<PoiModel>().DeleteAsync(p => p.Id == poiId);
        }

        public async Task<int> DeleteContentsByPoiIdAsync(int poiId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<ContentModel>().DeleteAsync(c => c.PoiId == poiId);
        }

        public async Task<int> DeleteAudiosByPoiIdAsync(int poiId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<VinhKhanh.Shared.AudioModel>().DeleteAsync(a => a.PoiId == poiId);
        }

        public async Task<int> DeleteContentByIdAsync(int contentId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<ContentModel>().DeleteAsync(c => c.Id == contentId);
        }

        public async Task<int> DeleteAudioByIdAsync(int audioId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<VinhKhanh.Shared.AudioModel>().DeleteAsync(a => a.Id == audioId);
        }

        public async Task<int> PrunePoisNotInSnapshotAsync(IEnumerable<int> serverPoiIds)
        {
            await EnsureInitializedAsync();

            var snapshot = new HashSet<int>((serverPoiIds ?? Enumerable.Empty<int>()).Where(id => id > 0));
            if (!snapshot.Any()) return 0;
            var allPois = await _database.Table<PoiModel>().ToListAsync();
            var staleIds = allPois
                .Where(p => p != null && p.Id > 0 && !snapshot.Contains(p.Id))
                .Select(p => p.Id)
                .Distinct()
                .ToList();

            if (!staleIds.Any()) return 0;

            var deleted = 0;
            foreach (var poiId in staleIds)
            {
                deleted += await DeletePoiByIdAsync(poiId);
                await DeleteContentsByPoiIdAsync(poiId);
                await DeleteAudiosByPoiIdAsync(poiId);
            }

            return deleted;
        }
    }
}