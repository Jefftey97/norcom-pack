# NORCOM Launcher 2.0.45 bridge

This is the phase 0–2 bridge from the Loader handoff. It preserves the existing
native WPF interface and compiled BAML assembly identity. The editable 3.x
interface rebuild is a later release, not part of this bridge milestone.

## What changes

- The friend launcher no longer implicitly reads the owner's G: server pin.
- Unreadable authentication caches remain on disk and are not replaced by an
  empty session during automatic recovery. Recovery directories are retained.
- A pinned ECDSA P-256 key verifies the exact signed channel payload. The payload
  fixes product, channel, sequence, expiry, immutable repository URL, sizes and
  SHA256s. New installs reject expired, replayed and conflicting releases.
- Downloads are bounded, reject redirects, and extract exactly two permitted
  entries. Installed successor directories are immutable by package hash.
- A separate coordinator launches the candidate, verifies its exact process and
  startup acknowledgement, then atomically commits state. Failed attempts stop
  only the owned candidate and restore verified previous bytes or the bootstrap.
  Failed packages remain quarantined and the accepted sequence is retained.
- The UI stays disabled until its startup is committed. An independent candidate
  watchdog exits if the coordinator dies before commit. Reopening inspects pending
  recovery before the single-instance UI check. Normal shutdown joins the listener
  and releases its owned mutex.
- Signed updates run at idle startup and through Check for updates. Legacy
  background Play/pin update callbacks return without scheduling or falling back
  to unsigned updates. An uncertain game lock blocks updating.
- The desktop ships self-contained on .NET 10. SDK 10.0.403 and runtime/package
  10.0.14 were used. Core retains net8.0 as an additional target for the unchanged
  Installer project. Assembly identity stays 2.0.1.0; product version is 2.0.45.

## Trust and owner tools

`src/Norcom.Launcher.ReleaseTool` is an owner-only utility and is never included
in the friends' package. Signing material is DPAPI CurrentUser protected under
private ACLs. Only the public key map is embedded in Core. The production public
key fingerprint is:

`b2c23a722cf8014e73b351501f9d045c37517bd4bafa78651edbf0863fc5fbd9`

The private key and auth backups must never enter the repository or review ZIP.
The encrypted backup requires the original Windows user/profile protection; it
is not independently recoverable following arbitrary profile loss. There is no
automatic root-key rotation. Loss of that profile/key requires a separately
reviewed trust migration, never an unsigned fallback.

The new channel is `launcher/channel-v1.json`. It is not published by a build.
Keep the legacy unsigned `launcher/manifest.json` at the bridge version before
shipping successors. Existing old launchers cannot retroactively authenticate
their first hop; review and verify that legacy bridge distribution separately.
Never point the old channel directly at a 3.x successor.

## Bootstrap servicing boundary

Successor updates do **not** update the stable bootstrap or its bundled runtime.
Automatic bootstrap servicing is not implemented in this release. Before a
bootstrap/runtime maintenance rollout, implement and review a separate signed
maintenance transaction: verify new bootstrap bytes, stop the UI/coordinator,
hold the install lock, preserve old EXE/state, replace exact bytes, and update
the recorded BootstrapHash without resetting Sequence, AcceptedDigest,
quarantine or active identities. Test interruption and restoration at every
replacement boundary. Replacing the canonical EXE alone breaks fallback hash
verification. Deleting state is not a maintenance procedure.

Review .NET servicing releases and republish maintained bootstrap installers
through that procedure. Do not describe a permanently frozen bootstrap as safe.
Authenticode signing is not supplied by the release-envelope signature.

## Verification and limits

The Windows fixture project links the real SingleInstance implementation and
uses disposable process identities. It is never distributed. Native activation,
coordinator death, nonce/identity mismatch, malformed executable rejection,
rollback, quarantine, monotonic accepted sequence, damaged previous payload,
and 100 activate/close/reacquire cycles have regression coverage.

Three inherited tests are excluded from the selected local suite because they
perform real UDP, a Prism junction operation, or an unsafe PID-stop scenario:
NewServerListUsesTheResolvedPort, ExistingNorcomInstanceIsClaimedWithoutWipingSaves,
and StopOfAMissingProcessIsAlreadyDone. They are not reported as passed.

WPF verification uses a separate disposable public key and offline profile, not
the production signing key. The offline opt-in requires explicit state/game/
desktop paths and NORCOM_QA_OFFLINE=1. No Minecraft/server/authentication launch is
part of that check. The final evidence report records exact tested hashes.

The existing decompiled UI still emits compiler warnings. Login/gameplay and
long-duration soak are not claimed tested while Minecraft stays off. PE header
checks reject the observed invalid-image failure; they do not replace all Windows
loader validation or make synchronous Process.Start universally timeout-bounded.
Replay protection covers the remote channel against current local state, not an
attacker who replaces all user-owned state and binaries. Ordinary auth writes
are not yet a fully transactional storage migration.
