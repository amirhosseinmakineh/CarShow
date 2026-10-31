using CarShow.Domain.Models.ZeroVehicles;
using Microsoft.EntityFrameworkCore;
namespace CarShow.Infrastracture.Context;

internal static class ZeroVehicleConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ZeroVehicleModel>(e =>
        {
            e.ToTable("ZeroVehicleModels");
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Aliases).HasMaxLength(2000);
            e.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<ZeroVehicleTrim>(e =>
        {
            e.ToTable("ZeroVehicleTrims");
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Aliases).HasMaxLength(2000);
            e.HasIndex(x => new { x.ModelId, x.Name }).IsUnique();
            e.HasAlternateKey(x => new { x.Id, x.ModelId });
            e.HasOne(x => x.Model).WithMany(x => x.Trims).HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ZeroVehicleListing>(e =>
        {
            e.ToTable("ZeroVehicleListings");
            e.Property(x => x.Channel).HasMaxLength(64);
            e.Property(x => x.ExternalMessageId).HasMaxLength(32);
            e.Property(x => x.OriginalName).HasMaxLength(256);
            e.Property(x => x.SourceUrl).HasMaxLength(2048);
            e.Property(x => x.RawText).HasMaxLength(8000);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.PricingStatus).HasMaxLength(32);
            e.Property(x => x.PriceToman).HasPrecision(18, 2);
            e.HasIndex(x => new { x.Channel, x.ExternalMessageId }).IsUnique();
            e.HasIndex(x => new { x.ModelId, x.TrimId, x.PublishedAt });
            e.HasIndex(x => x.PublishedAt);
            e.HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Trim).WithMany().HasForeignKey(x => new { x.TrimId, x.ModelId }).HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ZeroVehiclePriceGroup>(e =>
        {
            e.ToTable("ZeroVehiclePriceGroups");
            e.HasIndex(x => new { x.ModelId, x.TrimId }).IsUnique();
            e.Property(x => x.ReferencePrice).HasPrecision(18, 2);
            e.Property(x => x.MinPrice).HasPrecision(18, 2);
            e.Property(x => x.MaxPrice).HasPrecision(18, 2);
            e.Property(x => x.Confidence).HasMaxLength(16);
            e.HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Trim).WithMany().HasForeignKey(x => new { x.TrimId, x.ModelId }).HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ZeroVehicleImportRun>(e =>
        {
            e.ToTable("ZeroVehicleImportRuns");
            e.Property(x => x.Channel).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.StopReason).HasMaxLength(128);
            e.Property(x => x.Error).HasMaxLength(1024);
            e.Property(x => x.RejectionCounts).HasMaxLength(2000);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.Channel, x.Status });
        });
    }
}
