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
			var listener = scope.ServiceProvider.GetRequiredService<JetstreamListener>();

			try
			{
				await listener.Listen(cursor, stoppingToken);
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				log.LogError(e, "Jetstream listener threw an exception, re-connecting...");
			}

			cursor = listener.Cursor;
		}
	}
}
