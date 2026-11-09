namespace Norcom.Launcher.Core.Services;

public enum AuthFailure
{
	None,
	NotConfigured,
	InvalidClientId,
	Cancelled,
	Network,
	MojangNotApproved,
	NoEntitlement,
	NoProfile,
	XboxNoAccount,
	XboxUnavailable,
	XboxAdult,
	XboxChild,
	XboxRejected,
	InteractionRequired,
	Rejected,
	StorageUnavailable
}
