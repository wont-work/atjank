using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Atjank.Core.Configuration;
using Atjank.Firehose.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Atjank.Firehose;

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
			var r = redis.GetDatabase();
			var savedCursor = await r.StringGetAsync(CursorKey);
			if (savedCursor.HasValue) Cursor = ulong.Parse(savedCursor.ToString());
		}

		var pipe = new Pipe();
		await Task.WhenAll(
			JetstreamReader(pipe.Writer, onConnect, ct),
			MessageHandler(pipe.Reader, ct),
			CursorPersistenceTimer(ct)
		);
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
			var read = await _ws.ReceiveAsync(pipe.GetMemory(4096), ct);
			pipe.Advance(read.Count);
			if (!read.EndOfMessage) continue;

			var res = await pipe.FlushAsync(ct);
			if (res.IsCompleted) break;
		}

		await pipe.CompleteAsync();
	}

	async Task MessageHandler(PipeReader pipe, CancellationToken ct = default)
	{
		await using var stream = pipe.AsStream();

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

				_messageCount++;
				Cursor = message.TimeUs;
			}
		}
	}

	async Task CursorPersistenceTimer(CancellationToken ct = default)
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(3), ct);

			log.LogTrace("Roughly {Count} msg/s", _messageCount / 3f);
			_messageCount = 0;

			var r = redis.GetDatabase();
			await r.StringSetAsync(CursorKey, Cursor.ToString(), flags: CommandFlags.FireAndForget);
		}
	}
}
