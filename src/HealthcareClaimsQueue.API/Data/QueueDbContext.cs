using HealthcareClaimsQueue.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareClaimsQueue.API.Data;

public class QueueDbContext : DbContext
{
    public QueueDbContext(DbContextOptions<QueueDbContext> options) : base(options) { }

    public DbSet<Queue> Queues { get; set; } = null!;
    public DbSet<Claim> Claims { get; set; } = null!;
    public DbSet<AppUser> AppUsers { get; set; } = null!;
    public DbSet<UserSession> UserSessions { get; set; } = null!;
    public DbSet<ReviewTask> ReviewTasks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Queue
        modelBuilder.Entity<Queue>(e =>
        {
            e.ToTable("queue");
            e.HasKey(x => x.QueueId).HasName("PK_queue");
            e.Property(x => x.QueueId).HasColumnName("queue_id");
            e.Property(x => x.QueueCode).HasColumnName("queue_code").HasMaxLength(20).IsRequired();
            e.Property(x => x.QueueName).HasColumnName("queue_name").HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.QueueCode).IsUnique();
        });

        // Claim
        modelBuilder.Entity<Claim>(e =>
        {
            e.ToTable("claim");
            e.HasKey(x => x.ClaimId).HasName("PK_claim");
            e.Property(x => x.ClaimId).HasColumnName("claim_id");
            e.Property(x => x.ClaimNumber).HasColumnName("claim_number").HasMaxLength(10).IsRequired();
            e.Property(x => x.MemberId).HasColumnName("member_id").HasMaxLength(20).IsRequired();
            e.Property(x => x.ProviderId).HasColumnName("provider_id").HasMaxLength(20).IsRequired();
            e.Property(x => x.BilledAmount).HasColumnName("billed_amount").HasPrecision(12, 2);
            e.Property(x => x.ServiceFrom).HasColumnName("service_from");
            e.Property(x => x.ServiceTo).HasColumnName("service_to");
            e.Property(x => x.ReceivedOn).HasColumnName("received_on");
            e.HasIndex(x => x.ClaimNumber).IsUnique();
        });

        // AppUser
        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("app_user");
            e.HasKey(x => x.UserId).HasName("PK_app_user");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(50).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
        });

        // UserSession
        modelBuilder.Entity<UserSession>(e =>
        {
            e.ToTable("user_session");
            e.HasKey(x => x.SessionId).HasName("PK_user_session");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.EndedAt).HasColumnName("ended_at");

            e.HasOne(x => x.User)
                .WithMany(x => x.UserSessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ReviewTask (the core table)
        modelBuilder.Entity<ReviewTask>(e =>
        {
            e.ToTable("review_task");
            e.HasKey(x => x.TaskId).HasName("PK_review_task");
            e.Property(x => x.TaskId).HasColumnName("task_id");
            e.Property(x => x.ClaimId).HasColumnName("claim_id");
            e.Property(x => x.QueueId).HasColumnName("queue_id");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.DueDate).HasColumnName("due_date");
            e.Property(x => x.AssignedToUserId).HasColumnName("assigned_to_user_id");
            e.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(30);
            e.Property(x => x.LockedByUserId).HasColumnName("locked_by_user_id");
            e.Property(x => x.LockedOn).HasColumnName("locked_on");
            e.Property(x => x.LockExpiresOn).HasColumnName("lock_expires_on");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ClosedAt).HasColumnName("closed_at");
            e.Property(x => x.ClosedByUserId).HasColumnName("closed_by_user_id");
            e.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);

            // Foreign keys
            e.HasOne(x => x.Claim)
                .WithMany(x => x.ReviewTasks)
                .HasForeignKey(x => x.ClaimId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Queue)
                .WithMany(x => x.ReviewTasks)
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.AssignedToUser)
                .WithMany(x => x.AssignedTasks)
                .HasForeignKey(x => x.AssignedToUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.AssignedByUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.LockedByUser)
                .WithMany(x => x.LockedTasks)
                .HasForeignKey(x => x.LockedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ClosedByUser)
                .WithMany(x => x.CompletedTasks)
                .HasForeignKey(x => x.ClosedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes for performance
            e.HasIndex(x => new { x.QueueId, x.Status })
                .HasDatabaseName("ix_review_task_queue_status");

            e.HasIndex(x => x.ClaimId)
                .HasDatabaseName("ix_review_task_claim");
        });
    }
}
