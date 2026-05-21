using Microsoft.EntityFrameworkCore;

namespace Misty.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each configuration assigns its entity to the correct SQL schema (SchemaNames.Users, SchemaNames.Comm, or SchemaNames.Msg) via ToTable()
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
