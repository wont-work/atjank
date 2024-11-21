using OpenTelemetry.Exporter;

namespace Atjank.Core.Configuration;

sealed class OpenTelemetryConfig
{
	public const string Section = "OpenTelemetry";

	public Uri? BaseUri { get; set; }
	public OtlpExportProtocol Protocol { get; set; } = OtlpExportProtocol.Grpc;
}
