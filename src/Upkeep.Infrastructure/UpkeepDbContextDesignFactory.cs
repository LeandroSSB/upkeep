using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Upkeep.Infrastructure;

/// <summary>Usado apenas pelo `dotnet ef` (design-time).</summary>
public class UpkeepDbContextDesignFactory : IDesignTimeDbContextFactory<UpkeepDbContext>
{
    public UpkeepDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UpkeepDbContext>()
            .UseNpgsql("Host=localhost;Database=upkeep;Username=upkeep;Password=design")
            .Options;
        return new UpkeepDbContext(options);
    }
}
