using Npgsql;
using Quartz;

namespace Atjank.Worker;

public static class Builder
{
	public static IHostApplicationBuilder UseWorker(this IHostApplicationBuilder builder)
	{
		// i really do not like this
		var connectionString = builder.Configuration
			.GetSection("Database")
			.Get<NpgsqlConnectionStringBuilder>()!
			.ToString();

		builder.Services.AddOptions<QuartzOptions>().Configure(opt => opt.Scheduling.IgnoreDuplicates = true);

		builder.Services
			.AddQuartz(opt =>
			{
				opt.SchedulerName = "Atjank";
				opt.SchedulerId = "AUTO";
				opt.UsePersistentStore(opt =>
				{
					opt.UseProperties = true;
					opt.UsePostgres(opt => { opt.ConnectionString = connectionString; });
					opt.UseSystemTextJsonSerializer();
					opt.UseClustering();
				});
			});

		return builder;
	}
}
