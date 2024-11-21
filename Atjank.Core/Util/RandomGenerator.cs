using System.Security.Cryptography;

namespace Atjank.Core.Util;

static class RandomGenerator
{
	public static string Generate(int length = 22) =>
		RandomNumberGenerator.GetString("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz", length);
}
