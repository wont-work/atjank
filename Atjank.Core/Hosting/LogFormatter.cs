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
		writer.Write("\e[0;2m");

		if (requestId != null)
		{
			writer.Write(" [");
			writer.Write(requestId);
			writer.Write("]");
		}

		writer.Write(" [");
		writer.Write(category);
		writer.Write("]\e[0m ");
		writer.WriteLine(message);

		if (exception != null)
			writer.WriteLine(exception);
	}

	static string LevelIndicator(LogLevel level) => level switch
	{
		LogLevel.Warning => "\e[33mW",
		LogLevel.Error => "\e[31mE",
		LogLevel.Critical => "\e[1;31mC",
		LogLevel.Information => "\e[34mI",

		_ => " "
	};
}
