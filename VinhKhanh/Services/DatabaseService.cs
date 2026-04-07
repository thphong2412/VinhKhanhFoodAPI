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

        public DatabaseService()
        {
            // Kết nối đến file database SQLite trong thư mục cục bộ của App
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "VinhKhanh.db3");
            _database = new SQLiteAsyncConnection(dbPath);

            // Khởi tạo bảng đồng bộ (Dùng Task.Run để tránh treo giao diện)
            Task.Run(async () =>
            {
                await _database.CreateTableAsync<PoiModel>();
                await _database.CreateTableAsync<ContentModel>();
            });
        }

        // --- 1. LẤY DANH SÁCH ĐỊA ĐIỂM ---
        public async Task<List<PoiModel>> GetPoisAsync()
        {
            return await _database.Table<PoiModel>().ToListAsync();
        }

        // --- 2. LƯU ĐỊA ĐIỂM (QUÁN ĂN, TRẠM XE, DU LỊCH) ---
        public async Task<int> SavePoiAsync(PoiModel poi)
        {
            // Nếu đã có ID (khác 0) thì Cập nhật, nếu chưa có thì Thêm mới
            if (poi.Id != 0)
                return await _database.UpdateAsync(poi);
            else
                return await _database.InsertAsync(poi);
        }

        // --- 3. LƯU NỘI DUNG THUYẾT MINH ---
        public async Task<int> SaveContentAsync(ContentModel content)
        {
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
            return await _database.Table<ContentModel>()
                                  .Where(c => c.PoiId == poiId && c.LanguageCode == lang)
                                  .FirstOrDefaultAsync();
        }
    }
}