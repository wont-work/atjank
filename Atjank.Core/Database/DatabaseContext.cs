using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Npgsql;

namespace Atjank.Core.Database;

public sealed class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
	internal static void Configure(
		NpgsqlConnectionStringBuilder connectionStringBuilder,
		DbContextOptionsBuilder optionsBuilder
	)
	{
		// only multiplex if we're called directly as certain tooling like "dotnet ef" do not support it.
		if (connectionStringBuilder.Multiplexing)
		{
			connectionStringBuilder.Multiplexing =
				Assembly.GetEntryAssembly() == Assembly.GetAssembly(typeof(DatabaseContext));
		}

		var dataSource = new NpgsqlDataSourceBuilder(connectionStringBuilder.ToString());

		dataSource
			.EnableDynamicJson()
			.ConfigureJsonOptions(new JsonSerializerOptions
			{
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
				PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
			});

		optionsBuilder
			.UseNpgsql(dataSource.Build(), opt =>
				opt.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
					.SetPostgresVersion(17, 0)
					.EnableRetryOnFailure()
					.MigrationsHistoryTable("migrations")
			)
			.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution)
			.UseExceptionProcessor()
			.UseProjectables();

#if DEBUG
		optionsBuilder
			.EnableSensitiveDataLogging()
			.EnableDetailedErrors();
#endif
	}

	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();

		configurationBuilder.Properties<DateTime>().HavePrecision(0);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder) =>
		modelBuilder.AddQuartz(builder => builder.UsePostgreSql(schema: "public"));
}
