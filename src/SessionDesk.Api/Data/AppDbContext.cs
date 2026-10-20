using Microsoft.EntityFrameworkCore;

namespace SessionDesk.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Committee> Committees => Set<Committee>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<Hearing> Hearings => Set<Hearing>();
    public DbSet<BillAction> BillActions => Set<BillAction>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<User>().HasIndex(user => user.Email).IsUnique();

        modelBuilder.Entity<Committee>().ToTable("committees");

        modelBuilder.Entity<Assignment>().ToTable("assignments");
        modelBuilder.Entity<Assignment>().HasIndex(assignment => new { assignment.UserId, assignment.CommitteeId }).IsUnique();
        modelBuilder.Entity<Assignment>()
            .HasOne(assignment => assignment.User)
            .WithMany()
            .HasForeignKey(assignment => assignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Assignment>()
            .HasOne(assignment => assignment.Committee)
            .WithMany()
            .HasForeignKey(assignment => assignment.CommitteeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Bill>().ToTable("bills");
        modelBuilder.Entity<Bill>().HasIndex(bill => bill.Number).IsUnique();
        modelBuilder.Entity<Bill>()
            .HasOne(bill => bill.Committee)
            .WithMany()
            .HasForeignKey(bill => bill.CommitteeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Hearing>().ToTable("hearings");
        modelBuilder.Entity<Hearing>()
            .HasOne(hearing => hearing.Committee)
            .WithMany()
            .HasForeignKey(hearing => hearing.CommitteeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BillAction>().ToTable("bill_actions");
        modelBuilder.Entity<BillAction>()
            .HasOne(action => action.Bill)
            .WithMany()
            .HasForeignKey(action => action.BillId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BillAction>()
            .HasOne(action => action.Hearing)
            .WithMany()
            .HasForeignKey(action => action.HearingId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<BillAction>()
            .HasOne(action => action.Actor)
            .WithMany()
            .HasForeignKey(action => action.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditEvent>().ToTable("audit_events");
        modelBuilder.Entity<AuditEvent>()
            .HasOne(audit => audit.Actor)
            .WithMany()
            .HasForeignKey(audit => audit.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AuditEvent>()
            .HasOne(audit => audit.Bill)
            .WithMany()
            .HasForeignKey(audit => audit.BillId)
            .OnDelete(DeleteBehavior.SetNull);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties().Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)))
            {
                property.SetColumnType("timestamp with time zone");
            }
        }
    }
}
