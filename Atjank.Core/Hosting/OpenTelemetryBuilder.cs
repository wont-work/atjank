using System.Diagnostics;
using Atjank.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Atjank.Core.Hosting;

public static class OpenTelemetryBuilder
{
	const string SourceName = "Atjank";

	public static IHostApplicationBuilder UseOpenTelemetry(this IHostApplicationBuilder builder)
	{
		builder.Services.AddSingleton(new ActivitySource(SourceName, AtjankApp.Version));

		var otelCfg = builder.Configuration
			.GetSection(OpenTelemetryConfig.Section)
			.Get<OpenTelemetryConfig>();

		if (otelCfg?.BaseUri == null)
			return builder;

		builder.Logging.AddOpenTelemetry(opt =>
		{
			opt.SetResourceBuilder(ResourceBuilder.CreateDefault()
				.AddService(SourceName, serviceVersion: AtjankApp.Version));
		});

		builder.Services
			.AddOpenTelemetry()
			.ConfigureResource(opt =>
				opt.AddService(SourceName, serviceVersion: AtjankApp.Version))
			.UseOtlpExporter(otelCfg.Protocol, otelCfg.BaseUri)
			.WithMetrics(opt =>
				opt.AddAspNetCoreInstrumentation()
					.AddRuntimeInstrumentation()
					.AddHttpClientInstrumentation()
					.AddFusionCacheInstrumentation(opt =>
					{
						opt.IncludeBackplane = true;
						opt.IncludeDistributedLevel = true;
						opt.IncludeMemoryLevel = true;
					})
			)
			.WithTracing(opt =>
				opt.AddSource(SourceName)
					.AddAspNetCoreInstrumentation(opt => opt.EnrichWithHttpRequest = (t, req) =>
					{
						t.AddTag("http.request.id", req.HttpContext.TraceIdentifier);
					})
					.AddHttpClientInstrumentation()
					.AddEntityFrameworkCoreInstrumentation(opt => opt.SetDbStatementForText = true)
					.AddRedisInstrumentation(opt => opt.SetVerboseDatabaseStatements = true)
					.AddFusionCacheInstrumentation(opt => opt.IncludeBackplane = true)
					.AddQuartzInstrumentation()
			);

		return builder;
	}
}
