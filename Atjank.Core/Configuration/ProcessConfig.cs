namespace Atjank.Core.Configuration;

public sealed class ProcessConfig
{
	public const string Section = "Process";

	public int Id { get; set; }
	public bool WebWithWorker { get; set; } = true;
	public bool WebWithFirehose { get; set; } = true;
}
