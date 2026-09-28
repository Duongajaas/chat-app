using Microsoft.EntityFrameworkCore;

namespace ChatApp.Common;

public static class ApplicationConfiguration
{
    // Shared by the application and EF tooling; no database connection is opened here.
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var projectDirectory = FindProjectDirectory();
        var envFiles = new[]
        {
            projectDirectory == null ? null : Path.Combine(projectDirectory, ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), ".env")
        };
        foreach (var path in envFiles.Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path)) DotNetEnv.Env.NoClobber().Load(path!);
        }

        return WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(ApplicationConfiguration).Assembly.GetName().Name,
            ContentRootPath = projectDirectory ?? Directory.GetCurrentDirectory()
        });
    }

    public static string GetDatabaseConnection(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "Set ConnectionStrings__DefaultConnection in the environment or in ChatApp/.env. " +
                "Docker .env.production variables are not automatically mapped outside Docker Compose.");
        return connection;
    }

    public static void ConfigureDatabase(DbContextOptionsBuilder options, IConfiguration configuration) =>
        options.UseNpgsql(GetDatabaseConnection(configuration)).UseSnakeCaseNamingConvention();

    private static string? FindProjectDirectory()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                foreach (var candidate in new[] { directory.FullName, Path.Combine(directory.FullName, "ChatApp") })
                    if (File.Exists(Path.Combine(candidate, "ChatApp.csproj"))) return candidate;
            }
        }
        return null;
    }
}
