using Microsoft.EntityFrameworkCore;
using VinhKhanh.Shared;

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
        public DbSet<VinhKhanh.API.Models.User> Users { get; set; }
        public DbSet<VinhKhanh.API.Models.OwnerRegistration> OwnerRegistrations { get; set; }

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

                // Quan hệ 1-N với ContentModel
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

            // Các bảng log và hệ thống khác
            modelBuilder.Entity<VinhKhanh.Shared.TraceLog>(entity => { entity.HasKey(e => e.Id); });
            modelBuilder.Entity<VinhKhanh.Shared.AudioModel>(entity => { entity.HasKey(e => e.Id); });
        }
    }
}