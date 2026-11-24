using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class MachineSpecsProbeTests : IDisposable
{
	public void Dispose()
	{
		MachineSpecsProbe.Override = null;
	}

	[Fact]
	public async Task ReadAsyncProbesOffTheCallerAndCachesOneResult()
	{
		int calls = 0;
		var entered = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		try
		{
			MachineSpecsProbe.Override = () =>
			{
				Interlocked.Increment(ref calls);
				entered.Set();
				Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
				return new MachineSpecs { GpuName = "Once", LogicalCores = 8 };
			};
			Task<MachineSpecs> first = MachineSpecsProbe.ReadAsync();
			Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
			Assert.False(first.IsCompleted);
			Task<MachineSpecs> second = MachineSpecsProbe.ReadAsync();
			Assert.False(second.IsCompleted);
			Assert.Equal(1, Volatile.Read(ref calls));
			release.Set();
			MachineSpecs probed = await first;
			MachineSpecs shared = await second;
			Assert.Equal(1, calls);
			Assert.Equal("Once", probed.GpuName);
			Assert.Equal("Once", shared.GpuName);
			Assert.True(MachineSpecsProbe.TryGetCached(out MachineSpecs cached));
			Assert.Equal("Once", cached.GpuName);
			Assert.Equal("Once", MachineSpecsProbe.Read().GpuName);
			Assert.Equal(1, calls);
		}
		finally
		{
			release.Set();
			entered.Dispose();
			release.Dispose();
		}
	}

	[Fact]
	public async Task FailedProbeFallsBackOnceAndIsCached()
	{
		int calls = 0;
		MachineSpecsProbe.Override = () =>
		{
			calls++;
			throw new InvalidOperationException("no adapter");
		};
		MachineSpecs first = await MachineSpecsProbe.ReadAsync();
		MachineSpecs second = MachineSpecsProbe.Read();
		Assert.Equal(1, calls);
		Assert.Contains("detection failed", first.Gaps[0]);
		Assert.Contains("InvalidOperationException", first.Gaps[0]);
		Assert.Equal(first.Gaps[0], second.Gaps[0]);
		Assert.True(first.LogicalCores > 0);
		Assert.Equal("Low", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), second).Preset);
	}

	[Fact]
	public void ChangingTheProbeOverrideReadsAgain()
	{
		int calls = 0;
		MachineSpecsProbe.Override = () =>
		{
			calls++;
			return new MachineSpecs { GpuName = "First" };
		};
		Assert.Equal("First", MachineSpecsProbe.Read().GpuName);
		MachineSpecsProbe.Override = () =>
		{
			calls++;
			return new MachineSpecs { GpuName = "Second" };
		};
		Assert.Equal("Second", MachineSpecsProbe.Read().GpuName);
		Assert.Equal(2, calls);
		Assert.Equal("Second", MachineSpecsProbe.Read().GpuName);
		Assert.Equal(2, calls);
	}
}
