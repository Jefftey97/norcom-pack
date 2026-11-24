namespace Norcom.Launcher.Core.Services;

// A manual graphics tier chosen while Auto is still reading the PC replaces that Auto result.
public sealed class GraphicsChoiceGate
{
	private int _generation;

	public int Capture() => _generation;

	public void NoteManual() => _generation++;

	public bool IsCurrent(int captured) => captured == _generation;
}
