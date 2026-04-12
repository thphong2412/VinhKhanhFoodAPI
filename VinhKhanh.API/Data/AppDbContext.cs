using Microsoft.EntityFrameworkCore;
using VinhKhanh.Shared;

namespace VinhKhanh.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<PoiModel> PointsOfInterest { get; set; }
        public DbSet<ContentModel> PointContents { get; set; }
        public DbSet<VinhKhanh.Shared.TraceLog> TraceLogs { get; set; }
        public DbSet<VinhKhanh.Shared.AudioModel> AudioFiles { get; set; }
        public DbSet<VinhKhanh.Shared.TourModel> Tours { get; set; }
        public DbSet<VinhKhanh.API.Models.User> Users { get; set; }
        public DbSet<VinhKhanh.API.Models.OwnerRegistration> OwnerRegistrations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Cấu hình bảng PointsOfInterest
            modelBuilder.Entity<PoiModel>(entity =>
            {
                entity.ToTable("PointsOfInterest"); // Khớp với tên bảng trong SQL
                entity.HasKey(e => e.Id);
            });

            // Cấu hình bảng PointContents
            modelBuilder.Entity<ContentModel>(entity =>
            {
                entity.ToTable("PointContents"); // Khớp với tên bảng trong SQL
                entity.HasKey(e => e.Id);

                // ĐÂY LÀ CHỖ QUAN TRỌNG: Chỉ định rõ PoiId là khóa ngoại
                entity.HasOne<PoiModel>()
                      .WithMany(p => p.Contents)
                      .HasForeignKey(c => c.PoiId);
            });

            modelBuilder.Entity<VinhKhanh.Shared.TraceLog>(entity =>
            {
                entity.ToTable("TraceLogs");
                entity.HasKey(e => e.Id);
            });
        }
    }
}