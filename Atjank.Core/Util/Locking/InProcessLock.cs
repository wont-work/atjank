using AsyncKeyedLock;

namespace Atjank.Core.Util.Locking;

sealed class InProcessLock : ILock
{
	static readonly AsyncKeyedLocker<string> Locker = new();

	public ValueTask<IDisposable> LockAsync(string key) => Locker.LockAsync(key);
}
