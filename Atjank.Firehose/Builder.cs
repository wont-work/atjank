namespace Atjank.Firehose;

public static class Builder
{
	public static IHostApplicationBuilder UseFirehose(this IHostApplicationBuilder builder)
	{
		builder.Services.AddScoped<Jetstream>();

		return builder;
	}
}
