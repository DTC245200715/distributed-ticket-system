using DistributedTicketSystem.AssignmentWorker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistributedTicketSystem.AssignmentWorker.Data;

/// <summary>
/// DbContext riêng của Assignment Worker, phục vụ việc tra cứu và cập nhật Ticket/TicketHistory.
/// </summary>
public class WorkerDbContext : DbContext
{
    public WorkerDbContext(DbContextOptions<WorkerDbContext> options) : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketHistory> TicketHistories => Set<TicketHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.ToTable("Tickets");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.UserId).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Title).HasMaxLength(250).IsRequired();
            entity.Property(t => t.Description).IsRequired();

            entity.Property(t => t.Priority)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(t => t.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(t => t.AssignedTo).HasMaxLength(100);
            entity.Property(t => t.AssignedGroup).HasMaxLength(100);

            entity.Property(t => t.CreatedAt)
                  .HasColumnType("timestamp with time zone")
                  .IsRequired();

            entity.Property(t => t.UpdatedAt)
                  .HasColumnType("timestamp with time zone")
                  .IsRequired();

            // Khóa lạc quan (xmin)
            entity.Property(t => t.Version).IsRowVersion();

            entity.HasMany(t => t.Histories)
                  .WithOne(h => h.Ticket)
                  .HasForeignKey(h => h.TicketId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketHistory>(entity =>
        {
            entity.ToTable("TicketHistories");
            entity.HasKey(h => h.Id);

            entity.Property(h => h.PreviousStatus)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.Property(h => h.NewStatus)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(h => h.AssignedTo).HasMaxLength(100);
            entity.Property(h => h.AssignedGroup).HasMaxLength(100);
            entity.Property(h => h.ChangedBy).HasMaxLength(100).IsRequired();
            entity.Property(h => h.Reason).HasMaxLength(500);

            entity.Property(h => h.ChangedAt)
                  .HasColumnType("timestamp with time zone")
                  .IsRequired();
        });
    }
}
