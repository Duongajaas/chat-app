using ChatApp.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChatApp.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = ApplicationConfiguration.CreateBuilder(args);
        var options = new DbContextOptionsBuilder<AppDbContext>();
        ApplicationConfiguration.ConfigureDatabase(options, builder.Configuration);
        return new AppDbContext(options.Options);
    }
}
