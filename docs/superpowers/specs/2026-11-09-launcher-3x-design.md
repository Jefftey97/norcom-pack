# NORCOM Launcher 3.0 desktop redesign

User directive: finish the full launcher after the installed2.0.45 bridge. Existing
delegation covers routine design decisions. Codex implements; Astra reviews;
Grok reviews the exact candidate before local installation. Minecraft stays off.

## Selected approach

Editable native WPF on the existing .NET10 stack. A WebView renderer would add
an unnecessary runtime and security boundary; patching compiled BAML would keep
the existing maintainability problem. Replace the compiled legacy UI coherently
with real App, Shell, Home, Settings and theme XAML. Retain Core and the existing
view-model commands. Keep assembly identity2.0.1.0 while product version becomes
3.0.0. Keep the actual550b866 bootstrap, trust key and protocol1/state unchanged.

## Experience

Two pages: Home and Settings. Home gives one obvious Play action, game-stop
control only while playing, honest server status/player count, pack preparation,
Prism account guidance, news and useful folder/community links. Settings groups
graphics presets, RAM, accessibility/sound preferences, repair and About/update/
diagnostics. Prism owns the real Minecraft account; no invented signed-in badge
or owner/server controls. Failed ping means unknown, never verified stopped.

NORCOM tokens: background#090d16,surface#111827,line#2a3549,text#f2f5fa,
muted#a3afc3,blue#4db8ff,pink#ff4fa3,focus#9edbff. Crisp text, restrained chrome,
clear keyboard focus and labels, native resizing, scrollable small layouts,
reduced motion, textual states alongside color. No high-frequency animation loop.

## Runtime and state

Program authenticates activation before constructing WPF. UI starts disabled,
signals health after ContentRendered and enables actions only after commit.
Timers, shortcut repair and background work start after this gate, are bounded
and stop on close. Server ping runs off the UI thread. Existing settings/auth/
Prism/saves paths and schemas remain unchanged. No migration is needed. The
successor must be readable/recoverable by the unchanged550 bootstrap.

Crash reports remain local unless explicitly sent by the user. No automatic
queue flush. Logs/diagnostics are available through deliberate actions. Retain
the explicit offline verification guard, with no Play/repair/auth/network during
checks. Old legacy account controls and disabled close-to-tray preference are
not falsely presented as functioning Prism controls.

## Delivery and validation

Fresh verified59-file backup precedes edits (parent outputs/NORCOM-Launcher-3x-
backup.json). Isolated source branchlauncher-3x-20261109. Test behavior and actual
compiled XAML bindings/layout; reuse222 selected backend/native cases and3
standing exclusions. Exercise Home/Settings/keyboard/small-window/reduced-motion
and real550-bootstrap→signed3.x activation/rollback using disposable profiles.
Freeze exact source/package hashes, obtain Astra and Grok reviews, sign the
reviewed package, activate locally beneath the existing bootstrap, then inspect
the installed UI and verify settings/sentinels. Public feed rollout, friend-PC
acceptance and owner status publisher are separate operations; browser/workstation
and live Minecraft remain outside this task.
