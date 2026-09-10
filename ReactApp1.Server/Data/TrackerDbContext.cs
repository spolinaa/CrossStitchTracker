using Microsoft.EntityFrameworkCore;
using PatternTracker.Server.Models;

namespace PatternTracker.Server.Data;

public class TrackerDbContext : DbContext
{
    public TrackerDbContext(DbContextOptions<TrackerDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Pattern> Patterns => Set<Pattern>();
    public DbSet<PatternColor> PatternColors => Set<PatternColor>();
    public DbSet<PatternCell> PatternCells => Set<PatternCell>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(x => x.YandexPsuid).IsUnique();
            b.Property(x => x.YandexPsuid).IsRequired();
        });

        modelBuilder.Entity<Pattern>(b =>
        {
            b.HasOne(x => x.Owner)
                .WithMany(u => u.Patterns)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PatternColor>(b =>
        {
            b.HasOne<Pattern>()
                .WithMany(p => p.Colors)
                .HasForeignKey(x => x.PatternId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.PatternId, x.ColorIndex }).IsUnique();
        });

        modelBuilder.Entity<PatternCell>(b =>
        {
            b.HasOne<Pattern>()
                .WithMany(p => p.Cells)
                .HasForeignKey(x => x.PatternId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.PatternId, x.Page, x.Y, x.X }).IsUnique();
        });
    }
}
