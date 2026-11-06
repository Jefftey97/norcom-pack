using System;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public static class RepairRun
{
	public static async Task<string> ExecuteAsync(TransferMeter meter, Func<CancellationToken, Task> syncPack, Func<CancellationToken, Task> ensureJava, CancellationToken ct)
	{
		RepairTrace.Begin();
		meter.SetPhase("Checking Java", 0.08, 0.12);
		RepairTrace.SetStage("Java");
		try
		{
			await ensureJava(ct).ConfigureAwait(false);
			meter.SetPhase("Java 25 is ready", 0.25, 0.05);
		}
		catch (Exception ex)
		{
			string why = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message.Trim();
			RepairTrace.NoteJava(why);
			meter.SetPhase("Java check failed", 0.25, 0.05);
		}
		RepairTrace.SetStage("pack resync");
		meter.SetPhase("Repairing the friends pack", 0.35, 0.55);
		await syncPack(ct).ConfigureAwait(false);
		meter.SetPhase("Repair finished", 1.0, 0.0);
		return RepairTrace.SuccessLine();
	}
}
