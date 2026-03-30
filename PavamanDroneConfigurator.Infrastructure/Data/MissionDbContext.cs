using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.Infrastructure.Data.Entities;

namespace PavamanDroneConfigurator.Infrastructure.Data;

public class MissionDbContext : DbContext
{
    public const string DefaultConnectionString = "Data Source=mission_drafts.db";

    public MissionDbContext(DbContextOptions<MissionDbContext> options)
        : base(options)
    {
    }

    public DbSet<MissionDraftEntity> Drafts => Set<MissionDraftEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(DefaultConnectionString);
        }
    }
}
