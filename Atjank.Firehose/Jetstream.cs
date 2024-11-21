using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Atjank.Core.Configuration;
using Atjank.Firehose.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Atjank.Firehose;

// HACK
file sealed record JetstreamExceptionMessage : JetstreamMessage
{
	public required Exception Exception { get; init; }
}

sealed class Jetstream(
	ILogger<Jetstream> log,
	IOptionsSnapshot<GeneralConfig> cfg,
	HttpClient http,
	IConnectionMultiplexer redis
) : IDisposable
{
	const string CursorKey = "jetstream-cursor";

	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		RespectNullableAnnotations = true,
		AllowOutOfOrderMetadataProperties = true
	};

	readonly Uri _endpoint = cfg.Value.Jetstream;

	readonly Channel<JetstreamMessage> _jobs =
		Channel.CreateBounded<JetstreamMessage>(new BoundedChannelOptions(cfg.Value.MessageConcurrency) { SingleWriter = true });

	readonly IDatabase _redis = redis.GetDatabase();
	readonly ClientWebSocket _ws = new();

	ulong _messageCount;

	public ulong Cursor { get; set; }

	public void Dispose() => _ws.Dispose();

	public async Task Listen(Func<Task>? onConnect, ulong cursor = default, CancellationToken ct = default)
	{
		if (cursor != default)
		{
			Cursor = cursor;
		}
		else
		{
			var savedCursor = await _redis.StringGetAsync(CursorKey);
			if (savedCursor.HasValue) Cursor = (ulong)savedCursor;
		}

		try
		{
			var pipe = new Pipe();
			await Task.WhenAll(
				JetstreamReader(pipe.Writer, onConnect, ct),
				MessageQueuer(pipe.Reader, ct),
				Overseer(ct),
				CursorPersistenceTimer(ct)
			);
		}
		finally
		{
			await _redis.StringSetAsync(CursorKey, Cursor, flags: CommandFlags.FireAndForget);
		}
	}

	public async Task Send(SubscriberSourcedMessage msg, CancellationToken ct = default)
	{
		if (log.IsEnabled(LogLevel.Trace))
			log.LogTrace("Sending {Json}", JsonSerializer.Serialize(msg, JsonOptions));

		var data = JsonSerializer.SerializeToUtf8Bytes(msg, JsonOptions);
		await _ws.SendAsync(data, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, ct);
	}

	async Task JetstreamReader(PipeWriter pipe, Func<Task>? onConnect, CancellationToken ct = default)
	{
		List<string> query = ["?requireHello=true"];
		if (Cursor != default) query.Add($"cursor={Cursor}");
		var endpoint = new Uri(_endpoint, string.Join('&', query));
		log.LogInformation("Connecting to Jetstream at {Endpoint}", endpoint);

		_ws.Options.HttpVersion = HttpVersion.Version20;
		_ws.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
		await _ws.ConnectAsync(endpoint, http, ct);

		log.LogDebug("Connected.");
		if (onConnect != null) await onConnect();

		while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
		{
			var read = await _ws.ReceiveAsync(pipe.GetMemory(16 * 1024), ct);
			pipe.Advance(read.Count);
			if (!read.EndOfMessage) continue;

			var res = await pipe.FlushAsync(ct);
			if (res.IsCompleted) break;
		}

		await pipe.CompleteAsync();
	}

	async Task MessageQueuer(PipeReader pipe, CancellationToken ct = default)
	{
		await using var stream = pipe.AsStream();
		var queue = _jobs.Writer;

		while (!ct.IsCancellationRequested)
		{
			// without this .Catch, exceptions just stop the loop altogether and don't throw until cancellation
			var messages = JsonSerializer.DeserializeAsyncEnumerable<JetstreamMessage>(stream, true, JsonOptions, ct)
				.Catch<JetstreamMessage?, Exception>(e =>
					AsyncEnumerableEx.Return<JetstreamMessage>(new JetstreamExceptionMessage
					{
						Did = "", TimeUs = 0, Exception = e
					}));

			await foreach (var message in messages)
			{
				if (message == null)
				{
					log.LogWarning("Could not deserialize Jetstream message (unknown exception)");
					continue;
				}

				if (message is JetstreamExceptionMessage exc)
				{
					if (exc.Exception is not OperationCanceledException)
						log.LogWarning(exc.Exception, "Could not deserialize Jetstream message");

					continue;
				}

				_messageCount++;
				Cursor = Math.Max(Cursor, message.TimeUs);

				await queue.WaitToWriteAsync(ct);
				await queue.WriteAsync(message, ct);
			}
		}
	}

	async Task Overseer(CancellationToken ct = default)
	{
		await Task.WhenAll(Enumerable.Repeat(() => Worker(ct), cfg.Value.MessageConcurrency).Select(Task.Run));
	}

	async Task CursorPersistenceTimer(CancellationToken ct = default)
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(3), ct);

			log.LogTrace("Roughly {Count} msg/s ingested", _messageCount / 3f);
			_messageCount = 0;

			await _redis.StringSetAsync(CursorKey, Cursor, flags: CommandFlags.FireAndForget);
		}
	}

	async Task Worker(CancellationToken ct)
	{
		var queue = _jobs.Reader;

		try
		{
			await foreach (var message in queue.ReadAllAsync(ct))
			{
				// TODO
			}
		}
		catch (OperationCanceledException)
		{
			/* ignore */
		}
	}
}
