using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Infrastracture.Context
{
    public class CarShowContext : DbContext
    {
        public CarShowContext(DbContextOptions<CarShowContext> options) : base(options)
        {

        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicitly send zero-valued prices instead of treating them as database-generated.
            modelBuilder.Entity<Car>(entity =>
            {
                entity.Property(x => x.FactoryPrice).HasColumnType("decimal(18,2)").ValueGeneratedNever();
                entity.Property(x => x.PriceSampleCount).ValueGeneratedNever();
                entity.Property(x => x.Slug).IsRequired();
                entity.Property(x => x.YearCalendar).IsRequired();
            });
        }

        public DbSet<User> Users { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Car> Cars { get; set; }
        public DbSet<Tip> Tips { get; set; }
        public DbSet<Model> CarModels { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
    }
}
