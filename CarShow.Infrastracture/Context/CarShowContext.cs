using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Infrastracture.Context
{
    public class CarShowContext : DbContext
    {
        public CarShowContext(DbContextOptions<CarShowContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<UserRole> UserRoles { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<Car> Cars { get; set; } = null!;
        public DbSet<Tip> Tips { get; set; } = null!;
        public DbSet<CarModel> CarModels { get; set; } = null!;
        public DbSet<Company> Companies { get; set; } = null!;
        public DbSet<CarPriceHistory> CarPriceHistories { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The initial database migration created this column as TipName.
            // Keep the CLR property named Name while targeting the existing schema.
            modelBuilder.Entity<Tip>()
                .Property(x => x.Name)
                .HasColumnName("TipName");

            modelBuilder.Entity<Car>(entity =>
            {
                entity.Property(x => x.MarketPrice).HasColumnType("decimal(18,2)");
                entity.Property(x => x.FactoryPrice).HasColumnType("decimal(18,2)");

                entity.HasOne(x => x.Company)
                    .WithMany(x => x.Cars)
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.CarModel)
                    .WithMany(x => x.Cars)
                    .HasForeignKey(x => x.CarModelId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Tip)
                    .WithMany(x => x.Cars)
                    .HasForeignKey(x => x.TipId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<CarPriceHistory>(entity =>
            {
                entity.Property(x => x.MarketPrice).HasColumnType("decimal(18,2)");
                entity.Property(x => x.FactoryPrice).HasColumnType("decimal(18,2)");

                entity.HasOne(x => x.Car)
                    .WithMany()
                    .HasForeignKey(x => x.CarId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
