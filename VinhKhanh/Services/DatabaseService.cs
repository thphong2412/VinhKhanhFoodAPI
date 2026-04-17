using SQLite;
using VinhKhanh.Shared;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

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
            // Kiểm tra xem đã có nội dung cho điểm này chưa
            var existing = await _database.Table<ContentModel>()
                .Where(c => c.PoiId == content.PoiId && c.LanguageCode == content.LanguageCode)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                content.Id = existing.Id; // Giữ nguyên ID cũ để Update
                return await _database.UpdateAsync(content);
            }
            return await _database.InsertAsync(content);
        }

        // --- 4. LẤY NỘI DUNG THEO ID VÀ NGÔN NGỮ (Dùng để đọc thuyết minh) ---
        public async Task<ContentModel> GetContentByPoiIdAsync(int poiId, string lang)
        {
            await EnsureInitializedAsync();
            return await _database.Table<ContentModel>()
                                  .Where(c => c.PoiId == poiId && c.LanguageCode == lang)
                                  .FirstOrDefaultAsync();
        }

        // --- Audio helpers ---
        public async Task<int> SaveAudioAsync(VinhKhanh.Shared.AudioModel audio)
        {
            await EnsureInitializedAsync();
            if (audio == null) return 0;
            if (audio.Id != 0) return await _database.UpdateAsync(audio);
            return await _database.InsertAsync(audio);
        }

        public async Task<VinhKhanh.Shared.AudioModel> GetAudioByPoiAndLangAsync(int poiId, string lang)
        {
            await EnsureInitializedAsync();
            return await _database.Table<VinhKhanh.Shared.AudioModel>()
                .Where(a => a.PoiId == poiId && a.LanguageCode == lang)
                .FirstOrDefaultAsync();
        }

        public async Task<System.Collections.Generic.List<VinhKhanh.Shared.AudioModel>> GetAudiosByPoiAsync(int poiId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<VinhKhanh.Shared.AudioModel>()
                .Where(a => a.PoiId == poiId)
                .ToListAsync();
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
    }
}