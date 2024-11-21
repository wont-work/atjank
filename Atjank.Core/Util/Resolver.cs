using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Atjank.Core.Configuration;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atjank.Core.Util;

// https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/commit/15d955c478bf394cab21d2a7ed6e7715d261168e/Iceshrimp.Backend/Core/Services/CustomHttpClient.cs#L43
[UsedImplicitly]
sealed class Resolver(ILogger<Resolver> log, IOptionsMonitor<SecurityConfig> securityCfg)
{
	const int ConnectionBackoff = 75;

	[SuppressMessage("Blocker Bug", "S2930:\"IDisposables\" should be disposed")]
	[SuppressMessage("Usage", "CA2201:Do not raise reserved exception types")]
	public async ValueTask<Stream> ConnectCallback(SocketsHttpConnectionContext context, CancellationToken token)
	{
		var insecure = securityCfg.CurrentValue.AllowLocalConnections;
		var sortedRecords = await GetSortedAddresses(context.DnsEndPoint.Host, token);

		using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(token);
		var tasks = new List<Task<(NetworkStream? stream, Exception? exception)>>();

		var delayCts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken.Token);
		for (var i = 0; i < sortedRecords.Count; i++)
		{
			var record = sortedRecords[i];

			if (record.IsIPv4MappedToIPv6)
				record = record.MapToIPv4();

			if (!insecure)
			{
				if (record.IsLoopback())
				{
					log.LogWarning("Refusing to connect to loopback address {Address} due to possible SSRF",
						record.ToString());
					continue;
				}

				if (record.IsLocalIPv6())
				{
					log.LogWarning("Refusing to connect to local IPv6 address {Address} due to possible SSRF",
						record.ToString());
					continue;
				}

				if (record.IsLocalIPv4())
				{
					log.LogWarning("Refusing to connect to local IPv4 address {Address} due to possible SSRF",
						record.ToString());
					continue;
				}
			}

			delayCts.CancelAfter(ConnectionBackoff * i);

			var task = AttemptConnection(
				record,
				context.DnsEndPoint.Port,
				linkedToken.Token,
				delayCts.Token
			);
			tasks.Add(task);

			var nextDelayCts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken.Token);
			_ = task.ContinueWith((_, _) => nextDelayCts.Cancel(), TaskContinuationOptions.OnlyOnFaulted,
				TaskScheduler.Current);
			delayCts = nextDelayCts;
		}

		if (tasks.Count == 0)
			throw new Exception($"Can't connect to {context.DnsEndPoint.Host}: no candidate addresses remaining");

		NetworkStream? stream = null;
		Exception? lastException = null;

		while (tasks.Count > 0 && stream == null)
		{
			var task = await Task.WhenAny(tasks).ConfigureAwait(false);
			var res = await task;
			tasks.Remove(task);
			stream = res.stream;
			lastException = res.exception;
		}

		if (stream == null)
		{
			throw lastException ??
			      new Exception("An unknown exception occured during fast fallback connection attempt");
		}

		await linkedToken.CancelAsync();
		tasks.ForEach(task =>
			_ = task.ContinueWith((_, _) => Task.CompletedTask, CancellationToken.None, TaskScheduler.Current)
		);

		return stream;
	}

	static async Task<(NetworkStream? stream, Exception? exception)> AttemptConnection(
		IPAddress address,
		int port,
		CancellationToken token,
		CancellationToken delayToken
	)
	{
		try
		{
			await Task.Delay(-1, delayToken).ConfigureAwait(false);
		}
		catch (TaskCanceledException)
		{
			/* ignore */
		}

		token.ThrowIfCancellationRequested();

		var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
		{
			NoDelay = true
		};

		try
		{
			await socket.ConnectAsync(address, port, token).ConfigureAwait(false);
			return (new NetworkStream(socket, true), null);
		}
		catch (Exception e)
		{
			socket.Dispose();
			return (null, e);
		}
	}

	static async Task<List<IPAddress>> GetSortedAddresses(string hostname, CancellationToken token)
	{
		// This method abuses DNS ordering and LINQ a bit. We can normally assume that addresses will be provided in
		// the order the system wants to use. GroupBy will return its groups *in the order they're discovered*. Meaning,
		// the first group created will always be the preferred group, and all other groups are in preference order.
		// This means a straight zipper merge is nice and clean and gives us most -> least preferred, repeating.
		var dnsRecords = await Dns.GetHostAddressesAsync(hostname, AddressFamily.Unspecified, token);

		var groups = dnsRecords
			.GroupBy(a => a.AddressFamily)
			.Select(g => g.Select(v => v))
			.ToArray();

		return ZipperMerge(groups).ToList();
	}

	static IEnumerable<TSource> ZipperMerge<TSource>(params IEnumerable<TSource>[] sources)
	{
		// Adapted from https://github.com/KazWolfe/Dalamud/blob/767cc49ecb80e29dbdda2fa8329d3c3341c964fe/Dalamud/Utility/Util.cs
		var enumerators = new IEnumerator<TSource>[sources.Length];
		try
		{
			for (var i = 0; i < sources.Length; i++)
				enumerators[i] = sources[i].GetEnumerator();

			var hasNext = new bool[enumerators.Length];

			bool MoveNext() => enumerators
				.Select((t, i) => hasNext[i] = t.MoveNext())
				.Aggregate(false, (current, v) => current || v);

			while (MoveNext())
			{
				for (var i = 0; i < enumerators.Length; i++)
				{
					if (hasNext[i])
						yield return enumerators[i].Current;
				}
			}
		}
		finally
		{
			foreach (var enumerator in enumerators)
				enumerator.Dispose();
		}
	}
}

file static class IpAddressExtensions
{
	public static bool IsLoopback(this IPAddress address) => IPAddress.IsLoopback(address);

	public static bool IsLocalIPv6(this IPAddress address) =>
		address.AddressFamily == AddressFamily.InterNetworkV6
		&& (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal);

	public static bool IsLocalIPv4(this IPAddress address) =>
		address.AddressFamily == AddressFamily.InterNetwork
		&& IsPrivateIPv4(address.GetAddressBytes());

	static bool IsPrivateIPv4(byte[] ipv4Bytes)
	{
		return IsLinkLocal() || IsClassA() || IsClassC() || IsClassB();

		// Link local (no IP assigned by DHCP): 169.254.0.0 to 169.254.255.255 (169.254.0.0/16)
		bool IsLinkLocal() => ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254;

		// Class A private range: 10.0.0.0 – 10.255.255.255 (10.0.0.0/8)
		bool IsClassA() => ipv4Bytes[0] == 10;

		// Class B private range: 172.16.0.0 – 172.31.255.255 (172.16.0.0/12)
		bool IsClassB() => ipv4Bytes[0] == 172 && ipv4Bytes[1] >= 16 && ipv4Bytes[1] <= 31;

		// Class C private range: 192.168.0.0 – 192.168.255.255 (192.168.0.0/16)
		bool IsClassC() => ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168;
	}
}
