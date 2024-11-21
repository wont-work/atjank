#if DEBUG
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Atjank.Core.Database;

[UsedImplicitly]
sealed class DesignTimeDatabaseContext : IDesignTimeDbContextFactory<DatabaseContext>
{
	public DatabaseContext CreateDbContext(string[] args)
	{
		var builder = new ConfigurationBuilder();
		builder.SetBasePath($"{AtjankApp.Dir}/../../../"); // i mildly dislike this

		var configs = Environment.GetEnvironmentVariable("ATJANK_Configs") ?? "config.ini,config.override.ini";
		foreach (var config in configs.Split(',',
			         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
			builder.AddIniFile(config, false, true);

		builder.AddEnvironmentVariables("ATJANK_");

		var configuration = builder.Build();
		var opts = new DbContextOptionsBuilder<DatabaseContext>();
		DatabaseContext.Configure(configuration.GetSection("Database").Get<NpgsqlConnectionStringBuilder>()!, opts);
		return new DatabaseContext(opts.Options);
	}
}
#endif
