namespace Atjank.Core.Util.Locking;

interface ILock
{
	ValueTask<IDisposable> LockAsync(string key);
}
