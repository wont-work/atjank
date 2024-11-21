using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Atjank.Core.Configuration;
using Atjank.Firehose.Models;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

namespace Atjank.Firehose;

[UsedImplicitly]
sealed class JetstreamListener(ILogger<JetstreamListener> log, IOptionsSnapshot<AppViewConfig> cfg)
{
	const int Buffer = 1024;
	const int MaximumBuffer = 8192;

	readonly Uri _endpoint = cfg.Value.Jetstream;

	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		RespectNullableAnnotations = true,
		AllowOutOfOrderMetadataProperties = true
	};

	public ulong Cursor { get; set; }

	public async Task Listen(ulong cursor = default, CancellationToken ct = default)
	{
		if (cursor != default)
			Cursor = cursor;

		var jsInPipe = new Pipe();
		await Task.WhenAll(
			JetstreamReader(jsInPipe.Writer, ct),
			MessageHandler(jsInPipe.Reader, ct),
			CursorPersistenceTimer(ct)
		);
	}

	async Task JetstreamReader(PipeWriter pipe, CancellationToken ct = default)
	{
		List<string> query = [$"?maxMessageSizeBytes={MaximumBuffer}"];
		if (Cursor != default) query.Add($"cursor={Cursor}");
		var endpoint = new Uri(_endpoint, string.Join('&', query));
		log.LogInformation("Connecting to Jetstream at {Endpoint}", endpoint);

		using var handler = new SocketsHttpHandler();
		using var ws = new ClientWebSocket();
		ws.Options.HttpVersion = HttpVersion.Version30;
		ws.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
		await ws.ConnectAsync(endpoint, new HttpMessageInvoker(handler), ct);

		log.LogDebug("Connected.");

		while (!ct.IsCancellationRequested)
		{
			var read = await ws.ReceiveAsync(pipe.GetMemory(Buffer), ct);
			pipe.Advance(read.Count);
			if (!read.EndOfMessage) continue;

			var res = await pipe.FlushAsync(ct);
			if (res.IsCanceled || res.IsCompleted) break;
		}

		await pipe.CompleteAsync();
	}

	async Task MessageHandler(PipeReader pipe, CancellationToken ct = default)
	{
		var stream = pipe.AsStream();

		while (!ct.IsCancellationRequested)
		{
			await foreach (var message in JsonSerializer.DeserializeAsyncEnumerable<JetstreamMessage>(stream, true,
				               JsonOptions, ct))
			{
				if (message == null)
				{
					log.LogWarning("Could not deserialize a Jetstream message");
					continue;
				}

				log.LogTrace("Jetstream: {Message}", message);
				Cursor = message.TimeUs;
			}
		}
	}

	async Task CursorPersistenceTimer(CancellationToken ct = default)
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(5), ct);

			log.LogInformation("// TODO: save cursor value");
		}
	}
}
