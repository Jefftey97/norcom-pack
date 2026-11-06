using System;

namespace Norcom.Launcher.Core.Services;

public sealed class UpdateRejectedException : Exception
{
	public string Token { get; }

	public UpdateRejectedException(string token)
		: base(token)
	{
		Token = token;
	}
}
