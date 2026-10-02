using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Upkeep;

namespace Upkeep.Infrastructure;

public class UpkeepDbContext(DbContextOptions<UpkeepDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<MaintenanceTemplate> Templates => Set<MaintenanceTemplate>();
    public DbSet<ServiceRecord> Services => Set<ServiceRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.NtfyTopic).HasMaxLength(64);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.ExpiresAt).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Asset>(e =>
        {
            e.ToTable("assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Notas).HasMaxLength(2000);
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<MaintenanceTemplate>(e =>
        {
            e.ToTable("maintenance_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Categoria).HasMaxLength(64);
            e.Property(x => x.BaselineData).IsRequired();
            e.HasIndex(x => x.AssetId);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId);
        });

        b.Entity<ServiceRecord>(e =>
        {
            e.ToTable("service_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Notas).HasMaxLength(2000);
            e.HasIndex(x => x.AssetId);
            e.HasIndex(x => x.TemplateId);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId);
            e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId);
        });
    }
}

public static class UpkeepDbContextExtensions
{
    public static IServiceCollection AddUpkeepDbContext(
        this IServiceCollection services, string connectionString) =>
        services.AddDbContext<UpkeepDbContext>(o => o.UseNpgsql(connectionString));

    /// <summary>
    /// Asset pelo id ESCOPADO ao dono — o padrão de ownership repetido nos endpoints de
    /// assets/templates/services (null = não existe OU é de outro usuário → 404 sem vazar qual).
    /// </summary>
    public static Task<Asset?> GetOwnedAssetAsync(
        this UpkeepDbContext db, Guid id, Guid userId, CancellationToken ct = default) =>
        db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
}
