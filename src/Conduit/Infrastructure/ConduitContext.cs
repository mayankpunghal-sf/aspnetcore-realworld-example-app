using System;
using System.Data;
using Conduit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Conduit.Infrastructure;

public class ConduitContext(DbContextOptions options) : DbContext(options)
{
    private IDbContextTransaction? _currentTransaction;

    public DbSet<Article> Articles { get; init; } = null!;
    public DbSet<Comment> Comments { get; init; } = null!;
    public DbSet<Person> Persons { get; init; } = null!;
    public DbSet<Tag> Tags { get; init; } = null!;
    public DbSet<ArticleTag> ArticleTags { get; init; } = null!;
    public DbSet<ArticleFavorite> ArticleFavorites { get; init; } = null!;
    public DbSet<FollowedPeople> FollowedPeople { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // timestamps are stored as UTC; restore the DateTimeKind lost by providers like SQLite so
        // they serialize with the trailing 'Z' the RealWorld spec relies on
        modelBuilder.Entity<Article>(b =>
        {
            b.Property(x => x.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            b.Property(x => x.UpdatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });

        modelBuilder.Entity<Comment>(b =>
        {
            b.Property(x => x.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            b.Property(x => x.UpdatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });

        modelBuilder.Entity<ArticleTag>(b =>
        {
            b.HasKey(t => new { t.ArticleId, t.TagId });

            b.HasOne(pt => pt.Article)
                .WithMany(p => p.ArticleTags)
                .HasForeignKey(pt => pt.ArticleId);

            b.HasOne(pt => pt.Tag).WithMany(t => t.ArticleTags).HasForeignKey(pt => pt.TagId);
        });

        modelBuilder.Entity<ArticleFavorite>(b =>
        {
            b.HasKey(t => new { t.ArticleId, t.PersonId });

            b.HasOne(pt => pt.Article)
                .WithMany(p => p.ArticleFavorites)
                .HasForeignKey(pt => pt.ArticleId);

            b.HasOne(pt => pt.Person)
                .WithMany(t => t.ArticleFavorites)
                .HasForeignKey(pt => pt.PersonId);
        });

        modelBuilder.Entity<FollowedPeople>(b =>
        {
            b.HasKey(t => new { t.ObserverId, t.TargetId });

            // we need to add OnDelete RESTRICT otherwise for the SqlServer database provider,
            // app.ApplicationServices.GetRequiredService<ConduitContext>().Database.EnsureCreated(); throws the following error:
            // System.Data.SqlClient.SqlException
            // HResult = 0x80131904
            // Message = Introducing FOREIGN KEY constraint 'FK_FollowedPeople_Persons_TargetId' on table 'FollowedPeople' may cause cycles or multiple cascade paths.Specify ON DELETE NO ACTION or ON UPDATE NO ACTION, or modify other FOREIGN KEY constraints.
            // Could not create constraint or index. See previous errors.
            b.HasOne(pt => pt.Observer)
                .WithMany(p => p.Followers)
                .HasForeignKey(pt => pt.ObserverId)
                .OnDelete(DeleteBehavior.Restrict);

            // we need to add OnDelete RESTRICT otherwise for the SqlServer database provider,
            // app.ApplicationServices.GetRequiredService<ConduitContext>().Database.EnsureCreated(); throws the following error:
            // System.Data.SqlClient.SqlException
            // HResult = 0x80131904
            // Message = Introducing FOREIGN KEY constraint 'FK_FollowingPeople_Persons_TargetId' on table 'FollowedPeople' may cause cycles or multiple cascade paths.Specify ON DELETE NO ACTION or ON UPDATE NO ACTION, or modify other FOREIGN KEY constraints.
            // Could not create constraint or index. See previous errors.
            b.HasOne(pt => pt.Target)
                .WithMany(t => t.Following)
                .HasForeignKey(pt => pt.TargetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // physical per-engine type mapping only; SQL Server keeps its defaults so its
        // behavior is unchanged. There is no provider logic anywhere in the features.
        if (Database.IsNpgsql())
        {
            // timestamps are written as UTC (DateTime.UtcNow), so on PostgreSQL they map to
            // timestamp with time zone; SQL Server keeps its datetime2 default
            modelBuilder.Entity<Article>(b =>
            {
                b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
                b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
            });

            modelBuilder.Entity<Comment>(b =>
            {
                b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
                b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
            });

            // case-insensitive equality on identity strings to match the SQL Server default
            // collation. The collation must exist in the database before EnsureCreated:
            // CREATE COLLATION conduit_ci (provider = icu, locale = 'und-u-ks-level2', deterministic = false);
            modelBuilder.Entity<Person>(b =>
            {
                b.Property(x => x.Username).UseCollation("conduit_ci");
                b.Property(x => x.Email).UseCollation("conduit_ci");
            });

            modelBuilder.Entity<Article>(b => b.Property(x => x.Slug).UseCollation("conduit_ci"));

            modelBuilder.Entity<Tag>(b => b.Property(x => x.TagId).UseCollation("conduit_ci"));

            modelBuilder.Entity<ArticleTag>(b =>
                b.Property(x => x.TagId).UseCollation("conduit_ci")
            );
        }
    }

    #region Transaction Handling
    public void BeginTransaction()
    {
        if (_currentTransaction != null)
        {
            return;
        }

        if (!Database.IsInMemory())
        {
            _currentTransaction = Database.BeginTransaction(IsolationLevel.ReadCommitted);
        }
    }

    public void CommitTransaction()
    {
        try
        {
            _currentTransaction?.Commit();
        }
        catch
        {
            RollbackTransaction();
            throw;
        }
        finally
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
        }
    }

    public void RollbackTransaction()
    {
        try
        {
            _currentTransaction?.Rollback();
        }
        finally
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
        }
    }
    #endregion
}
