using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Atjank.Core.Hosting;

sealed class LogFormatter() : ConsoleFormatter("Atjank")
{
	public override void Write<TState>(
		in LogEntry<TState> logEntry,
		IExternalScopeProvider? scopeProvider,
		TextWriter textWriter
	)
	{
		var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
		string? requestId = null;

		scopeProvider?.ForEachScope(
			(scope, _) =>
			{
				if (scope is not IEnumerable<KeyValuePair<string, object>> properties)
					return;

				foreach (var pair in properties)
				{
					if (pair.Key == "RequestId")
						requestId = pair.Value as string;
				}
			},
			logEntry.State
		);

		Write(textWriter, requestId, logEntry.Category, logEntry.LogLevel, message, logEntry.Exception);
	}

	static void Write(
		TextWriter writer,
		string? requestId,
		string category,
		LogLevel level,
		string message,
		Exception? exception
	)
	{
		writer.Write(LevelIndicator(level));
		writer.Write("\x1b[0;2m");

		if (requestId != null)
			writer.Write($" [{requestId}]");

		writer.Write($" [{category}]\x1b[0m ");
		writer.WriteLine(message);

		if (exception != null)
			writer.WriteLine(exception);
	}

	static string LevelIndicator(LogLevel level) => level switch
	{
		LogLevel.Warning => "\x1b[33mW",
		LogLevel.Error => "\x1b[31mE",
		LogLevel.Critical => "\x1b[1;31mC",
		LogLevel.Information => "\x1b[34mI",

		_ => " "
	};
}
