using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Atjank.Core.Configuration;

public static class ConfigBuilderExt
{
	public static IHostApplicationBuilder UseConfiguration(this IHostApplicationBuilder builder)
	{
		builder.Configuration.Sources.Clear();

		// try keeping in sync with Database/DesignTimeDatabaseContext.cs
		var configs = Environment.GetEnvironmentVariable("ATJANK_Configs") ?? "config.ini,config.override.ini";
		foreach (var config in configs.Split(',',
			         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
			builder.Configuration.AddIniFile(config, false, true);

		builder.Configuration.AddEnvironmentVariables("ATJANK_");

		builder.Services
			.AddOptions<GeneralConfig>()
			.BindConfiguration(GeneralConfig.Section)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		builder.Services
			.AddOptions<SecurityConfig>()
			.BindConfiguration(SecurityConfig.Section)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		builder.Services
			.AddOptions<OpenTelemetryConfig>()
			.BindConfiguration(OpenTelemetryConfig.Section)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		builder.Services
			.AddOptions<ProcessConfig>()
			.BindConfiguration(ProcessConfig.Section)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		builder.Services
			.AddOptions<NpgsqlConnectionStringBuilder>()
			.BindConfiguration("Database");

		return builder;
	}
}
