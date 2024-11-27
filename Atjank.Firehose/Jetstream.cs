using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
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

	readonly IDatabase _redis = redis.GetDatabase();
	readonly SemaphoreSlim _workerLimiter = new(cfg.Value.MessageConcurrency, cfg.Value.MessageConcurrency);

	readonly ConcurrentDictionary<long, Task> _workers = [];
	readonly ClientWebSocket _ws = new();

	ulong _messagesPer3Seconds;

	public ulong Cursor { get; set; }

	public void Dispose()
	{
		_ws.Dispose();
		_workerLimiter.Dispose();
	}

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
				CursorPersistenceTimer(ct)
			);
		}
		finally
		{
			await _redis.StringSetAsync(CursorKey, Cursor, flags: CommandFlags.FireAndForget);

			log.LogInformation("Waiting to complete {Count} running jobs...", _workers.Count);
			await Task.WhenAll(_workers.Values);
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
		var endpoint = new Uri(cfg.Value.Jetstream, string.Join('&', query));
		log.LogInformation("Connecting to Jetstream at {Endpoint}", endpoint);

		_ws.Options.HttpVersion = HttpVersion.Version20;
		_ws.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
		await _ws.ConnectAsync(endpoint, http, ct);

		log.LogDebug("Connected.");
		if (onConnect != null) await onConnect();

		try
		{
			while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
			{
				var read = await _ws.ReceiveAsync(pipe.GetMemory(16 * 1024), ct);
				pipe.Advance(read.Count);
				if (!read.EndOfMessage) continue;

				var res = await pipe.FlushAsync(ct);
				if (res.IsCompleted) break;
			}
		}
		finally
		{
			await pipe.CompleteAsync();
		}
	}

	async Task MessageQueuer(PipeReader pipe, CancellationToken ct = default)
	{
		await using var stream = pipe.AsStream();

		while (!ct.IsCancellationRequested)
		{
			// without this .Catch, exceptions just stop the loop altogether and don't throw until cancellation
			var messages = JsonSerializer.DeserializeAsyncEnumerable<JetstreamMessage>(stream, true, JsonOptions, ct)
				.Catch<JetstreamMessage?, Exception>(e =>
					AsyncEnumerableEx.Return<JetstreamMessage>(new JetstreamExceptionMessage
					{
						Did = "",
						TimeUs = 0,
						Exception = e
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

				_messagesPer3Seconds++;
				Cursor = Math.Max(Cursor, message.TimeUs);

				await _workerLimiter.WaitAsync(ct);
				_ = Task.Run(async () =>
				{
					var task = Worker(message);
					var id = Random.Shared.NextInt64();

					if (!_workers.TryAdd(id, task))
						log.LogWarning("Worker tracker key collision. Id {Id} isn't as unique as I thought it'd be", id);

					await task;

					_workers.Remove(id, out _);
				}, ct);
			}
		}
	}

	async Task CursorPersistenceTimer(CancellationToken ct = default)
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(3), ct);

			log.LogTrace(
				"Roughly {MessageCount} msg/s ingested - Current worker count: {WorkerCount} ({LimiterCount} slots left)",
				_messagesPer3Seconds / 3f, _workers.Count, _workerLimiter.CurrentCount
			);
			_messagesPer3Seconds = 0;

			await _redis.StringSetAsync(CursorKey, Cursor, flags: CommandFlags.FireAndForget);
		}
	}

	async Task Worker(JetstreamMessage message)
	{
		try
		{
			await Task.Delay(1); // todo: do work
		}
		finally
		{
			_workerLimiter.Release();
		}
	}
}
