using Atjank.Core.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Atjank.Core.Util.Locking;

sealed class RedisLock(IConnectionMultiplexer redisFactory, IOptionsMonitor<ProcessConfig> procCfg) : ILock
{
	static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);

	public async ValueTask<IDisposable> LockAsync(string key)
	{
		key = $"lock:{key}";

		var id = procCfg.CurrentValue.Id;
		var redis = redisFactory.GetDatabase();
		var delay = TimeSpan.FromMilliseconds(1);

		// StackExchange.Redis does not allow blocking redis operations, so we have to poll instead
		while (!await redis.LockTakeAsync(key, id, TimeSpan.FromMinutes(5)))
		{
			await Task.Delay(delay);

			if (delay <= MaxDelay)
				delay *= 2;
		}

		return new Releaser(redis, key, id);
	}

	sealed class Releaser(IDatabase redis, string key, int id) : IDisposable
	{
		public void Dispose() => redis.LockRelease(key, id, CommandFlags.FireAndForget);
	}
}
