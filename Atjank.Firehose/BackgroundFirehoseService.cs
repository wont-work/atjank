using Atjank.Core.Configuration;
using Atjank.Firehose.Models;
using Microsoft.Extensions.Options;

namespace Atjank.Firehose;

public sealed class BackgroundFirehoseService(ILogger<BackgroundFirehoseService> log, IServiceProvider svc)
	: BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		ulong cursor = default;

		while (!stoppingToken.IsCancellationRequested)
		{
			await using var scope = svc.CreateAsyncScope();
			var listener = scope.ServiceProvider.GetRequiredService<Jetstream>();
			var cfg = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<GeneralConfig>>();

			try
			{
				await listener.Listen(
					() => listener.Send(
						new SubscriberOptionsUpdateMessage
						{
							Payload = new SubscriberOptionsUpdateMessage.Data
							{
								WantedCollections = cfg.Value.WantedCollections.Split(',',
									StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
								MaxMessageSizeBytes = 16 * 1024
							}
						},
						stoppingToken),
					cursor, stoppingToken);
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				log.LogError(e, "Jetstream listener threw an exception, re-connecting...");
			}

			cursor = listener.Cursor;
		}
	}
}
