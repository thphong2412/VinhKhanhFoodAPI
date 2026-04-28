using Microsoft.EntityFrameworkCore;
using VinhKhanh.Shared;
using VinhKhanh.API.Models;

namespace VinhKhanh.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Đảm bảo tên DbSet này khớp với code Seed ở Program.cs
        public DbSet<PoiModel> PointsOfInterest { get; set; }
        public DbSet<ContentModel> PointContents { get; set; }
        public DbSet<VinhKhanh.Shared.TraceLog> TraceLogs { get; set; }
        public DbSet<VinhKhanh.Shared.AudioModel> AudioFiles { get; set; }
        public DbSet<VinhKhanh.Shared.TourModel> Tours { get; set; }
        public DbSet<VinhKhanh.Shared.PoiReviewModel> PoiReviews { get; set; }
        public DbSet<VinhKhanh.API.Models.User> Users { get; set; }
        public DbSet<VinhKhanh.API.Models.OwnerRegistration> OwnerRegistrations { get; set; }
        public DbSet<VinhKhanh.API.Models.PoiRegistration> PoiRegistrations { get; set; }
        public DbSet<VinhKhanh.API.Models.AiUsageLog> AiUsageLogs { get; set; }
        public DbSet<VinhKhanh.API.Models.LocalizationJobLog> LocalizationJobLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình cho PoiModel
            modelBuilder.Entity<PoiModel>(entity =>
            {
                entity.ToTable("PointsOfInterest");
                entity.HasKey(e => e.Id);

                // Vì ông dùng SQLite cho Dev, ta cấu hình Latitude/Longitude linh hoạt
                entity.Property(e => e.Latitude).IsRequired();
                entity.Property(e => e.Longitude).IsRequired();

                // Quan hệ 1-N với ContentModel - cascade delete khi POI bị xóa
                entity.HasMany(p => p.Contents)
                      .WithOne()
                      .HasForeignKey(c => c.PoiId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Cấu hình cho ContentModel
            modelBuilder.Entity<ContentModel>(entity =>
            {
                entity.ToTable("PointContents");
                entity.HasKey(e => e.Id);
            });

            // Cấu hình cho AudioModel - cascade delete khi POI bị xóa
            modelBuilder.Entity<VinhKhanh.Shared.AudioModel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne<PoiModel>()
                      .WithMany()
                      .HasForeignKey(a => a.PoiId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<VinhKhanh.Shared.PoiReviewModel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne<PoiModel>()
                      .WithMany()
                      .HasForeignKey(r => r.PoiId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Các bảng log và hệ thống khác
            modelBuilder.Entity<VinhKhanh.Shared.TraceLog>(entity => { entity.HasKey(e => e.Id); });
            modelBuilder.Entity<VinhKhanh.API.Models.AiUsageLog>(entity => { entity.HasKey(e => e.Id); });
            modelBuilder.Entity<VinhKhanh.API.Models.LocalizationJobLog>(entity => { entity.HasKey(e => e.Id); });
        }
    }
}