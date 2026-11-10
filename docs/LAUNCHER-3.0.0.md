# NORCOM Launcher 3.0

Editable native WPF Home/Settings replaces the preserved legacy BAML UI. Product
version 3.0.0 retains assembly identity 2.0.1.0 and the 550 bridge's protocol 1/state.
The existing 2.0.45 bootstrap stays installed at the canonical shortcut target;
it activates the signed immutable 3.0 successor and retains recovery authority.
Public feed rollout and a new-friend installer are separate distribution work.

Home presents Play/progress, pack status, aggregate server status, Prism account
guidance and news. Settings groups graphics/RAM/preferences, repair, updates,
logs and deliberate diagnostic transmission. Failed ping means Unknown. Explicit
status snapshots need a valid timestamp within 2 minutes; implicit owner-drive and
local status files are no longer imported. Offline QA remains explicitly gated.

Startup authentication happens before WPF construction. ContentRendered gates
the healthy commit; timers/actions start after acknowledgement. Status I/O is
off the UI thread and the entire TCP exchange is bounded to 2500 ms, with a 1 MiB
frame cap and checked string/varint bounds. UI work-area sizing handles small
high-DPI displays. Short navigation/splash motion respects preference flags.

Graphics mutations use game/QA guards and the same pin-operation semaphore.
New game locks retain actual process-start ticks. Stop refuses missing/mismatched
identity; verified PID reuse can clear a stale lock without stopping the unrelated
process. Old locks lacking identity remain conservative. No game/server action
is needed for validation. Updater journal fields/outcomes and trust are unchanged.

Settings/auth/game locations and schemas are reused without a migration. Unknown
settings keys remain preserved. Reports stay local unless the user explicitly
sends diagnostics. Real login/gameplay, another physical Win 10/Win 11 PC, actual
OS 200% DPI and long soak are not certified by the local/offline checks; the
automated 2x layout test is identified as simulated. Existing inherited compiler
warnings and the bridge's documented maintenance/trust limitations remain.

Preference hydration in 3.0 does not write settings; byte-preservation and nondefault
sound/motion/pause flags have a regression test. The unchanged 550 fallback UI has
an inherited hydration bug that may reset nondefault toggle preferences if it
is used after rollback. Auth/world bytes and unknown settings are retained. This
legacy limitation requires reviewed bootstrap servicing before broad rollout;
no claim that an unchanged 550 binary can be fixed by a 3.x source change is made.

Consent across fallback: 3.0 stores reports in Launcher/diagnostics-v3/outbox,
separate from the legacy Launcher/outbox scanned by 550. New private reports are
therefore not enumerated/sent by that fallback. Explicit manual delivery still
works; failed delivery asks the user to retry, never promises automatic retry.
Existing legacy-queue reports are untouched and may still be sent by 550; there
is no automatic migration between queues.
