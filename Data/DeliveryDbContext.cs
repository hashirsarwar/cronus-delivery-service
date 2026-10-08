using Cronus.DeliveryService.Models;
using Microsoft.EntityFrameworkCore;

namespace Cronus.DeliveryService.Data;

public class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public DbSet<Delivery> Deliveries => Set<Delivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var delivery = modelBuilder.Entity<Delivery>();

        delivery.HasKey(d => d.Id);
        delivery.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);
        delivery.HasIndex(d => d.OrderId).IsUnique();
    }
}
