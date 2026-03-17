using Microsoft.EntityFrameworkCore;
using VinhKhanhFoodAPI.Models;

namespace VinhKhanhFoodAPI
{
    public class FoodDbContext : DbContext
    {
        public FoodDbContext(DbContextOptions<FoodDbContext> options)
            : base(options)
        {
        }

        public DbSet<POI> POIs { get; set; }
        public DbSet<Visit> Visits { get; set; }
    }
}