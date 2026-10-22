using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;
using CarShow.Domain.Models.Telegram;

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
        public DbSet<TelegramChannel> TelegramChannels { get; set; } = null!;
        public DbSet<TelegramRawMessage> TelegramRawMessages { get; set; } = null!;
        public DbSet<CarListing> CarListings { get; set; } = null!;
        public DbSet<TelegramCollectionJob> TelegramCollectionJobs { get; set; } = null!;

        public DbSet<TelegramPricingState> TelegramPricingStates { get; set; } = null!;

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

            modelBuilder.Entity<TelegramChannel>(entity =>
            {
                entity.HasIndex(x => x.NormalizedIdentifier).IsUnique();
                entity.HasIndex(x => x.PublicUrl).IsUnique();
                entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
                entity.HasMany(x => x.RawMessages).WithOne(x => x.Channel).HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<TelegramRawMessage>(entity => entity.HasIndex(x => new { x.ChannelId, x.ExternalMessageId }).IsUnique());
            modelBuilder.Entity<CarListing>(entity =>
            {
                entity.HasIndex(x => new { x.ChannelId, x.SourceMessageId }).IsUnique();
                entity.HasIndex(x => x.RawMessageId).IsUnique().HasFilter("[RawMessageId] IS NOT NULL");
                entity.HasIndex(x => new { x.CarId, x.PriceDay });
                entity.Property(x => x.MatchStatus).HasMaxLength(32);
                entity.Property(x => x.Fingerprint).HasMaxLength(64);
                entity.Property(x => x.Price).HasPrecision(18, 2);
                entity.Property(x => x.NormalizedPrice).HasPrecision(18, 2);
                entity.HasOne(x => x.Car).WithMany().HasForeignKey(x => x.CarId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.RawMessage).WithMany().HasForeignKey(x => x.RawMessageId).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<TelegramCollectionJob>(entity =>
            {
                entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(64);
                entity.HasIndex(x => new { x.ChannelId, x.CreatedAt });
                entity.HasIndex(x => x.ChannelId).IsUnique()
                    .HasFilter("[Status] IN ('Queued', 'Processing')");
                entity.HasOne(x => x.Channel).WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CarPriceHistory>(entity =>
            {
                entity.Property(x => x.MarketPrice).HasColumnType("decimal(18,2)");
                entity.Property(x => x.FactoryPrice).HasColumnType("decimal(18,2)");
                entity.HasIndex(x => new { x.CarId, x.PriceDay }).IsUnique().HasFilter("[PriceDay] IS NOT NULL");

                entity.HasOne(x => x.Car)
                    .WithMany()
                    .HasForeignKey(x => x.CarId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
