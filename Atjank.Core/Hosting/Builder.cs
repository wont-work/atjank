using System.Net;
using Atjank.Core.Configuration;
using Atjank.Core.Database;
using Atjank.Core.Util;
using Atjank.Core.Util.Locking;
using IdGen;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.NeueccMessagePack;

namespace Atjank.Core.Hosting;

public static class Builder
{
	public static IHostApplicationBuilder UseCore(this IHostApplicationBuilder builder)
	{
		builder.Logging
			.ClearProviders()
			.AddConsole(opt => opt.FormatterName = "atjank")
			.AddConsoleFormatter<LogFormatter, ConsoleFormatterOptions>();

		builder
			.UseConfiguration();

		builder.Services
			.AddScoped<Urls>();

		var redisSection = builder.Configuration.GetSection(RedisConfig.Section);
		var redisConfig = redisSection.Get<RedisConfig>();

		if (redisConfig?.ConnectionString != null)
		{
			var redisOptions = ConfigurationOptions.Parse(redisConfig.ConnectionString);
			redisOptions.Protocol = RedisProtocol.Resp3;
			redisOptions.ClientName = "atjank";

			// neither the cache nor backplane support taking in the multiplexer from DI
			// i don't like this
			IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisOptions);
			builder.Services
				.AddSingleton(redis)
				.AddSingleton<ILock, RedisLock>()
				.AddStackExchangeRedisCache(opt => opt.ConnectionMultiplexerFactory = () => Task.FromResult(redis))
				.AddFusionCacheStackExchangeRedisBackplane(opt =>
					opt.ConnectionMultiplexerFactory = () => Task.FromResult(redis))
				.AddSingleton<IFusionCacheSerializer>(new FusionCacheNeueccMessagePackSerializer(
					new MessagePackSerializerOptions(CompositeResolver.Create(
							NativeDateTimeResolver.Instance,
							NativeGuidResolver.Instance,
							NativeDecimalResolver.Instance,
							AtjankIdMessagePackResolver.Instance,
							ContractlessStandardResolver.Instance,
							JsonNodeMessagePackResolver.Instance
						))
						.WithCompression(MessagePackCompression.Lz4BlockArray)));
		}
		else
		{
			builder.Services.AddSingleton<ILock, InProcessLock>();
		}

		builder.Services
			.AddFusionCache()
			.WithDefaultEntryOptions(new FusionCacheEntryOptions
			{
				Duration = TimeSpan.FromMinutes(5),
				JitterMaxDuration = TimeSpan.FromSeconds(5),

				DistributedCacheSoftTimeout = TimeSpan.FromSeconds(1),
				DistributedCacheHardTimeout = TimeSpan.FromSeconds(2),
				AllowBackgroundDistributedCacheOperations = true
			})
			.WithOptions(new FusionCacheOptions
			{
				DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(2),
				BackplaneCircuitBreakerDuration = TimeSpan.FromSeconds(2),

				DistributedCacheSyntheticTimeoutsLogLevel = LogLevel.Information,
				FactorySyntheticTimeoutsLogLevel = LogLevel.Information,
				FailSafeActivationLogLevel = LogLevel.Warning,
				SerializationErrorsLogLevel = LogLevel.Warning,
				DistributedCacheErrorsLogLevel = LogLevel.Warning,
				FactoryErrorsLogLevel = LogLevel.Error
			})
			.TryWithAutoSetup();

		// the client with the name "" gets injected for "HttpClient"
		builder.Services.AddSingleton<Resolver>();
		builder.Services
			.AddHttpClient("", (svc, opt) =>
			{
				var cfg = svc.GetRequiredService<IOptionsMonitor<AppViewConfig>>().CurrentValue;

				opt.DefaultRequestVersion = HttpVersion.Version20;
				opt.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
				opt.MaxResponseContentBufferSize = 1024 * 1024; // 1MiB
				opt.DefaultRequestHeaders.Add("User-Agent", $"Atjank/{AtjankApp.Version} (+{cfg.Url})");
				opt.Timeout = TimeSpan.FromSeconds(30);
			})
			.ConfigurePrimaryHttpMessageHandler(svc => new SocketsHttpHandler
			{
				MaxAutomaticRedirections = 2,
				AutomaticDecompression = DecompressionMethods.All,
				ConnectCallback = svc.GetRequiredService<Resolver>().ConnectCallback
			});

		builder.Services
			.AddSingleton(TimeProvider.System)
			.AddSingleton(svc => new IdGenerator(
				svc.GetRequiredService<IOptionsMonitor<ProcessConfig>>().CurrentValue.Id,
				new IdGeneratorOptions
				{
					SequenceOverflowStrategy = SequenceOverflowStrategy.SpinWait,
					TimeSource = new DefaultTimeSource(
						new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc))
				})
			)
			.AddPooledDbContextFactory<DatabaseContext>((svc, opt) =>
				DatabaseContext.Configure(svc.GetRequiredService<IOptions<NpgsqlConnectionStringBuilder>>().Value, opt))
			.AddScoped<DatabaseContext>(svc =>
				svc.GetRequiredService<IDbContextFactory<DatabaseContext>>().CreateDbContext());

		return builder;
	}
}
