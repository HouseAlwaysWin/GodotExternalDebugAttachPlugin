extends Node

## Waits on the main thread at startup so scene/C# _Ready runs *after* the wait.
## During a synchronous `_ready()` block the engine often does not paint the first frame yet,
## so Control overlays may never appear (gray/white window). Use window title + prints instead.

const PROJECT_SETTING_DEBUG_WAIT_SECONDS := "dotnet/external_debug_attach/debug_wait_seconds"
const LEGACY_DEBUG_WAIT := "external_debug_attach/debug_wait_seconds"

@export var max_wait_seconds: float = 12.0

var _saved_window_title: String = ""


func _ready() -> void:
	if Engine.is_editor_hint():
		return

	if not OS.is_debug_build():
		print("[DebugWait] Release build — skipping wait")
		return

	if ProjectSettings.has_setting(PROJECT_SETTING_DEBUG_WAIT_SECONDS):
		max_wait_seconds = float(ProjectSettings.get_setting(PROJECT_SETTING_DEBUG_WAIT_SECONDS))
	elif ProjectSettings.has_setting(LEGACY_DEBUG_WAIT):
		max_wait_seconds = float(ProjectSettings.get_setting(LEGACY_DEBUG_WAIT))

	if max_wait_seconds <= 0.0:
		print("[DebugWait] Wait disabled (debug_wait_seconds <= 0)")
		return

	print("[DebugWait] Blocking startup up to %.1fs (main scene loads after this)." % max_wait_seconds)
	print("[DebugWait] Watch the game WINDOW TITLE for countdown. Space = continue | Esc = skip")
	print("[DebugWait] If keys don’t respond, wait for timeout — input may not update during this phase.")

	var w := get_window()
	if w:
		_saved_window_title = w.title

	var deadline_usec := Time.get_ticks_usec() + int(max_wait_seconds * 1_000_000.0)
	var last_print_sec := -1

	while Time.get_ticks_usec() < deadline_usec:
		var remaining := (deadline_usec - Time.get_ticks_usec()) / 1_000_000.0
		remaining = max(remaining, 0.0)

		var up_sec := int(ceil(remaining))
		if w:
			w.title = "%s  |  DebugWait %ds  |  Space=go Esc=skip" % [_saved_window_title, up_sec]

		var sec_left := int(floor(remaining))
		if sec_left != last_print_sec:
			last_print_sec = sec_left
			print("[DebugWait] %ds left…" % up_sec)

		if Input.is_physical_key_pressed(KEY_ESCAPE):
			print("[DebugWait] User skipped (Esc)")
			break

		if Input.is_physical_key_pressed(KEY_SPACE):
			print("[DebugWait] User continued early (Space)")
			break

		OS.delay_usec(50_000)

	if w and _saved_window_title != "":
		w.title = _saved_window_title

	print("[DebugWait] Wait finished — loading main scene")
