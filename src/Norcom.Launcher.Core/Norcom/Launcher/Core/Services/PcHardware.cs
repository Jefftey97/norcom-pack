namespace Norcom.Launcher.Core.Services;

public sealed record PcHardware(string GpuName, long VramBytes, long RamBytes, int LogicalProcessors, bool Integrated);
