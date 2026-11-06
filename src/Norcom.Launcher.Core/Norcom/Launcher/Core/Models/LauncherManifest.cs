using System.Text.Json.Serialization;

namespace Norcom.Launcher.Core.Models;

public sealed class LauncherManifest
{
	[JsonPropertyName("version")]
	public string Version { get; set; } = "";


	[JsonPropertyName("minVersion")]
	public string? MinVersion { get; set; }

	[JsonPropertyName("sha256")]
	public string Sha256 { get; set; } = "";


	[JsonPropertyName("zipUrl")]
	public string ZipUrl { get; set; } = "";


	[JsonPropertyName("notes")]
	public string? Notes { get; set; }
}
