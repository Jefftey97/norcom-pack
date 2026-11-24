using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class GraphicsChoiceGateTests
{
	[Fact]
	public void AutoStaysCurrentWhenNoManualTierArrives()
	{
		GraphicsChoiceGate gate = new GraphicsChoiceGate();
		int captured = gate.Capture();
		Assert.True(gate.IsCurrent(captured));
	}

	[Fact]
	public void ManualTierDuringAutoProbeSupersedesThatAutoResult()
	{
		GraphicsChoiceGate gate = new GraphicsChoiceGate();
		int auto = gate.Capture();
		gate.NoteManual();
		Assert.False(gate.IsCurrent(auto));
		int later = gate.Capture();
		Assert.True(gate.IsCurrent(later));
		Assert.False(gate.IsCurrent(auto));
	}
}