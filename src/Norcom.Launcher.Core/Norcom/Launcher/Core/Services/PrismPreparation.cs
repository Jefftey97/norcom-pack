using System;
using System.Collections.Generic;

namespace Norcom.Launcher.Core.Services;

public sealed class PrismPreparation
{
	public bool CanStart { get; init; }

	public string Message { get; init; } = "";


	public string? Executable { get; init; }

	public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();


	public override string ToString()
	{
		return Message;
	}
}
