namespace Atjank.Core.Util.Extensions;

public static class StringExt
{
	public static string ToReversed(this string input)
	{
		return string.Create(input.Length, input, (chars, state) =>
		{
			// funnily StringBuilder.ToString().AsSpan().CopyTo() is somewhat
			// faster than StringBuilder.CopyTo()
			state.AsSpan().CopyTo(chars);
			chars.Reverse();
		});
	}
}
