// https://github.com/jcurl/RJCP.DLL.CodeQuality/blob/master/CodeQuality/NUnitExtensions/Trace/NUnitLoggerProvider.cs

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Atjank.Tests;

sealed class NUnitLogger<T> : ILogger<T>
{
	/// <summary>
	///     Logs at the specified log level.
	/// </summary>
	/// <typeparam name="TState">The type of the t state.</typeparam>
	/// <param name="logLevel">The log level.</param>
	/// <param name="eventId">The event identifier.</param>
	/// <param name="state">The state.</param>
	/// <param name="exception">The exception.</param>
	/// <param name="formatter">The formatter.</param>
	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter
	)
	{
		if (!IsEnabled(logLevel)) return;

		// Buffer the message into a single string in order to avoid shearing the message when running across multiple threads.
		StringBuilder messageBuilder = new();

		var timeStamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

		var linePrefix = $"[{timeStamp}] {typeof(T).FullName} {logLevel}: ";
		var lines = formatter(state, exception).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

		if (lines.Length == 0)
			messageBuilder.AppendLine(linePrefix);
		else
		{
			foreach (var line in lines)
				messageBuilder.Append(linePrefix).AppendLine(line);
		}

		lines = exception?.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries) ?? [];
		foreach (var line in lines) messageBuilder.Append(linePrefix).AppendLine(line);

		// Remove the last line-break, because ITestOutputHelper only has WriteLine.
		var message = messageBuilder.ToString();
		if (message.EndsWith(Environment.NewLine, StringComparison.Ordinal))
			message = message[..^Environment.NewLine.Length];

		try
		{
			TestContext.Out.WriteLine(message);
		}
		catch (Exception)
		{
			// We could fail because we're on a background thread and our captured ITestOutputHelper is
			// busted (if the test "completed" before the background thread fired).
			// So, ignore this. There isn't really anything we can do but hope the
			// caller has additional loggers registered
		}
	}

	/// <summary>
	///     Determines whether the specified log level is enabled.
	/// </summary>
	/// <param name="logLevel">The log level.</param>
	/// <returns>
	///     Is <see langword="true" /> if the specified log level is enabled; otherwise, <see langword="false" />.
	/// </returns>
	public bool IsEnabled(LogLevel logLevel) => true;

	/// <summary>
	///     Begins a new scope for logging.
	/// </summary>
	/// <typeparam name="TState">The type of the t state.</typeparam>
	/// <param name="state">The state.</param>
	/// <returns>An object to manage the scope.</returns>
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default;
}
