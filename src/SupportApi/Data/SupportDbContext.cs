using DistributedTicketSystem.SupportApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistributedTicketSystem.SupportApi.Data;

/// <summary>
/// DbContext cho Support API, quản lý tương tác với PostgreSQL qua EF Core.
/// </summary>
public class SupportDbContext : DbContext
{
    public SupportDbContext(DbContextOptions<SupportDbContext> options) : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketHistory> TicketHistories => Set<TicketHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cấu hình bảng Tickets
        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.ToTable("Tickets");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.UserId)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(t => t.Title)
                  .HasMaxLength(250)
                  .IsRequired();

            entity.Property(t => t.Description)
                  .IsRequired();

            // Lưu Enum dưới dạng chuỗi để dễ đọc và tương thích trực quan trong DB
            entity.Property(t => t.Priority)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(t => t.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(t => t.AssignedTo)
                  .HasMaxLength(100);

            entity.Property(t => t.AssignedGroup)
                  .HasMaxLength(100);

            entity.Property(t => t.CreatedAt)
                  .HasColumnType("timestamp with time zone")
                  .IsRequired();

            entity.Property(t => t.UpdatedAt)
                  .HasColumnType("timestamp with time zone")
                  .IsRequired();

            // Khóa lạc quan (Optimistic Concurrency)
            entity.Property(t => t.Version).IsRowVersion();

            // Đánh Index để tăng tốc độ truy vấn
            entity.HasIndex(t => t.UserId).HasDatabaseName("IX_Tickets_UserId");
            entity.HasIndex(t => t.Status).HasDatabaseName("IX_Tickets_Status");
            entity.HasIndex(t => t.Priority).HasDatabaseName("IX_Tickets_Priority");

            // Quan hệ 1-N: Một Ticket có nhiều TicketHistory
            entity.HasMany(t => t.Histories)
                  .WithOne(h => h.Ticket)
                  .HasForeignKey(h => h.TicketId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Cấu hình bảng TicketHistories
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

            entity.Property(h => h.AssignedTo)
                  .HasMaxLength(100);

            entity.Property(h => h.AssignedGroup)
                  .HasMaxLength(100);

            entity.Property(h => h.ChangedBy)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(h => h.Reason)
                  .HasMaxLength(500);

            entity.Property(h => h.ChangedAt)
                  .HasColumnType("timestamp with time zone")
                  .IsRequired();

            entity.HasIndex(h => h.TicketId).HasDatabaseName("IX_TicketHistories_TicketId");
            entity.HasIndex(h => h.ChangedAt).HasDatabaseName("IX_TicketHistories_ChangedAt");
        });
    }
}
