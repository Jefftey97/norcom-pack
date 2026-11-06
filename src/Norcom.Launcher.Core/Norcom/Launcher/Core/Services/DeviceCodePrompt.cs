namespace Norcom.Launcher.Core.Services;

public sealed record DeviceCodePrompt(string UserCode, string VerificationUri)
{
	public string Display => "Enter code " + UserCode + " at " + VerificationUri + ".";

	public override string ToString()
	{
		return Display;
	}
}
