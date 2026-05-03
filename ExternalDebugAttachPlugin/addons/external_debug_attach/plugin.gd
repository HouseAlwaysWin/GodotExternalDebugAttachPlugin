@tool
extends EditorPlugin

## Pure GDScript External Debug Attach Plugin
## All options live under Project Settings → Dotnet → External Debug Attach

const SERVICE_PORT := 47632
const SERVICE_HOST := "127.0.0.1"

## Shown under Project Settings → General → Dotnet → External Debug Attach
const SETTING_PREFIX := "dotnet/external_debug_attach/"
const SETTING_IDE_TYPE := SETTING_PREFIX + "ide_type"
const SETTING_VSCODE_PATH := SETTING_PREFIX + "vscode_path"
const SETTING_CURSOR_PATH := SETTING_PREFIX + "cursor_path"
const SETTING_ANTIGRAVITY_PATH := SETTING_PREFIX + "antigravity_path"
const SETTING_SHOW_SERVICE_CONSOLE := SETTING_PREFIX + "show_service_console"
const SETTING_AUTO_REGISTER_AUTOLOAD := SETTING_PREFIX + "auto_register_debugwait_autoload"
const SETTING_DEBUG_WAIT_SECONDS := SETTING_PREFIX + "debug_wait_seconds"
const SETTING_F5_ATTACH_CHECK_MAX := SETTING_PREFIX + "f5_attach_check_max"

## Legacy: Editor Settings (plugin ≤ 2.x) and ProjectSettings external_debug_attach/*
const LEGACY_SETTING_PREFIX := "external_debug_attach/"

## Register ProjectSettings inspector metadata only once per editor run (re-registering creates duplicate sidebar entries).
const META_PROJECT_INFOS := &"ExternalDebugAttachPlugin_dotnet_ps_infos_v2"

const AUTOLOAD_NAME := "DebugWait"
const AUTOLOAD_PATH := "res://addons/external_debug_attach/DebugWaitAutoload.gd"
const SERVICE_READY_QUICK_FRAMES := 30
const SERVICE_READY_WAIT_FRAMES := 300

enum IdeType {VSCode, Cursor, AntiGravity}

var _toolbar_box: HBoxContainer
var _button_main: Button
var _button_scene: Button
var _scene_dialog: EditorFileDialog
var _tcp_client: StreamPeerTCP
var _service_process_id: int = -1
## Tracks whether we spawned the listener with a visible CMD window (cmd /c start); background PID alone is unreliable.
var _service_started_with_console: bool = false
var _is_windows: bool = OS.get_name() == "Windows"


func _enter_tree() -> void:
	print("[ExternalDebugAttach] GDScript plugin loaded")
	if not _is_windows:
		push_warning("[ExternalDebugAttach] This plugin currently supports Windows only.")
		return

	_initialize_settings()

	_toolbar_box = HBoxContainer.new()
	_toolbar_box.add_theme_constant_override("separation", 2)

	_button_main = Button.new()
	_button_main.tooltip_text = "Run Main Scene + Attach Debug (Alt+F5)"
	_button_main.pressed.connect(_on_button_pressed)

	var icon = load("res://addons/external_debug_attach/attach_icon.svg")
	if icon:
		_button_main.icon = icon
	else:
		_button_main.text = "▶ Attach"

	var shortcut := Shortcut.new()
	var input_event := InputEventKey.new()
	input_event.keycode = KEY_F5
	input_event.alt_pressed = true
	shortcut.events = [input_event]
	_button_main.shortcut = shortcut
	_button_main.shortcut_in_tooltip = true

	_toolbar_box.add_child(_button_main)

	_button_scene = Button.new()
	_button_scene.tooltip_text = "Pick a scene, then Run + Attach Debug"
	_button_scene.pressed.connect(_on_scene_attach_button_pressed)
	var base_ctrl := get_editor_interface().get_base_control()
	if base_ctrl.get_theme_icon(&"PlayScene", &"EditorIcons") != null:
		_button_scene.icon = base_ctrl.get_theme_icon(&"PlayScene", &"EditorIcons")
	else:
		_button_scene.text = "Scene"
	_toolbar_box.add_child(_button_scene)

	add_control_to_container(CONTAINER_TOOLBAR, _toolbar_box)

	_scene_dialog = EditorFileDialog.new()
	_scene_dialog.title = "Run Scene + Attach Debug"
	_scene_dialog.file_mode = EditorFileDialog.FILE_MODE_OPEN_FILE
	_scene_dialog.access = EditorFileDialog.ACCESS_RESOURCES
	_scene_dialog.add_filter("*.tscn", "Godot Scene")
	_scene_dialog.file_selected.connect(Callable(self, &"_on_scene_selected_for_attach"))
	add_child(_scene_dialog)

	print("[ExternalDebugAttach] Ready (service & DebugWait start when you run / attach only)")


func _exit_tree() -> void:
	print("[ExternalDebugAttach] Unloading...")
	if not _is_windows:
		return

	if _button_main:
		_button_main.pressed.disconnect(_on_button_pressed)
	if _button_scene:
		_button_scene.pressed.disconnect(_on_scene_attach_button_pressed)
	if _scene_dialog:
		var cb := Callable(self, &"_on_scene_selected_for_attach")
		if _scene_dialog.file_selected.is_connected(cb):
			_scene_dialog.file_selected.disconnect(cb)
		_scene_dialog.queue_free()
		_scene_dialog = null
	if _toolbar_box:
		remove_control_from_container(CONTAINER_TOOLBAR, _toolbar_box)
		_toolbar_box.queue_free()
		_toolbar_box = null
	_button_main = null
	_button_scene = null

	if _tcp_client:
		_tcp_client.disconnect_from_host()
		_tcp_client = null

	# Kill the service process we started
	_kill_service()

	# Unregister Autoload only when auto-register mode is enabled
	if _is_auto_register_autoload_enabled():
		_unregister_autoload()

	print("[ExternalDebugAttach] Unloaded")


func _initialize_settings() -> void:
	_migrate_legacy_settings()
	_remove_legacy_project_keys_if_migrated()
	_cleanup_legacy_editor_settings()

	if not ProjectSettings.has_setting(SETTING_IDE_TYPE):
		ProjectSettings.set_setting(SETTING_IDE_TYPE, IdeType.VSCode)

	if not ProjectSettings.has_setting(SETTING_VSCODE_PATH):
		ProjectSettings.set_setting(SETTING_VSCODE_PATH, "")

	if not ProjectSettings.has_setting(SETTING_CURSOR_PATH):
		ProjectSettings.set_setting(SETTING_CURSOR_PATH, "")

	if not ProjectSettings.has_setting(SETTING_ANTIGRAVITY_PATH):
		ProjectSettings.set_setting(SETTING_ANTIGRAVITY_PATH, "")

	if not ProjectSettings.has_setting(SETTING_SHOW_SERVICE_CONSOLE):
		ProjectSettings.set_setting(SETTING_SHOW_SERVICE_CONSOLE, false)

	if not ProjectSettings.has_setting(SETTING_AUTO_REGISTER_AUTOLOAD):
		ProjectSettings.set_setting(SETTING_AUTO_REGISTER_AUTOLOAD, true)

	if not ProjectSettings.has_setting(SETTING_DEBUG_WAIT_SECONDS):
		ProjectSettings.set_setting(SETTING_DEBUG_WAIT_SECONDS, 12.0)

	if not ProjectSettings.has_setting(SETTING_F5_ATTACH_CHECK_MAX):
		ProjectSettings.set_setting(SETTING_F5_ATTACH_CHECK_MAX, 12)

	# Calling add_property_info on every plugin enable duplicates Dotnet → External Debug Attach in the tree.
	if not Engine.has_meta(META_PROJECT_INFOS):
		_register_all_project_property_infos()
		Engine.set_meta(META_PROJECT_INFOS, true)


func _migrate_legacy_settings() -> void:
	var editor := EditorInterface.get_editor_settings()
	var migrated := false

	var keys := [
		"ide_type",
		"vscode_path",
		"cursor_path",
		"antigravity_path",
		"show_service_console",
		"auto_register_debugwait_autoload",
		"debug_wait_seconds",
	]

	for k in keys:
		var new_key: String = SETTING_PREFIX + k
		if ProjectSettings.has_setting(new_key):
			continue

		var legacy_editor: String = LEGACY_SETTING_PREFIX + k
		var legacy_project: String = LEGACY_SETTING_PREFIX + k

		if editor.has_setting(legacy_editor):
			ProjectSettings.set_setting(new_key, editor.get_setting(legacy_editor))
			migrated = true
		elif ProjectSettings.has_setting(legacy_project):
			ProjectSettings.set_setting(new_key, ProjectSettings.get_setting(legacy_project))
			migrated = true

	if migrated:
		ProjectSettings.save()
		print(
			"[ExternalDebugAttach] Migrated settings to Project Settings → Dotnet → External Debug Attach "
			+ "(dotnet/external_debug_attach/*)."
		)


func _remove_legacy_project_keys_if_migrated() -> void:
	var keys := _legacy_setting_keys()
	var cleared := false
	for k in keys:
		var new_key: String = SETTING_PREFIX + k
		var legacy_project: String = LEGACY_SETTING_PREFIX + k
		if not ProjectSettings.has_setting(new_key):
			continue
		if not ProjectSettings.has_setting(legacy_project):
			continue
		if ProjectSettings.has_method(&"clear"):
			ProjectSettings.clear(legacy_project)
		else:
			ProjectSettings.set_setting(legacy_project, null)
		cleared = true
	if cleared:
		ProjectSettings.save()


func _cleanup_legacy_editor_settings() -> void:
	var es := EditorInterface.get_editor_settings()
	var keys := _legacy_setting_keys()
	var cleared := false
	for k in keys:
		var legacy_editor: String = LEGACY_SETTING_PREFIX + k
		if not es.has_setting(legacy_editor):
			continue
		es.erase(legacy_editor)
		cleared = true
	if cleared:
		es.save()
		print("[ExternalDebugAttach] Removed legacy Editor Settings keys (external_debug_attach/*). Restart Godot if the old category still appears in the UI.")


func _legacy_setting_keys() -> Array[String]:
	return [
		"ide_type",
		"vscode_path",
		"cursor_path",
		"antigravity_path",
		"show_service_console",
		"auto_register_debugwait_autoload",
		"debug_wait_seconds",
	]


func _register_all_project_property_infos() -> void:
	_add_project_setting_info(SETTING_IDE_TYPE, TYPE_INT, PROPERTY_HINT_ENUM, "VSCode,Cursor,AntiGravity")
	_add_project_setting_info(SETTING_VSCODE_PATH, TYPE_STRING, PROPERTY_HINT_GLOBAL_FILE, "*.exe")
	_add_project_setting_info(SETTING_CURSOR_PATH, TYPE_STRING, PROPERTY_HINT_GLOBAL_FILE, "*.exe")
	_add_project_setting_info(SETTING_ANTIGRAVITY_PATH, TYPE_STRING, PROPERTY_HINT_GLOBAL_FILE, "*.exe")
	_add_project_setting_info(SETTING_SHOW_SERVICE_CONSOLE, TYPE_BOOL, PROPERTY_HINT_NONE, "")
	_add_project_setting_info(SETTING_AUTO_REGISTER_AUTOLOAD, TYPE_BOOL, PROPERTY_HINT_NONE, "")
	_add_project_setting_info(SETTING_DEBUG_WAIT_SECONDS, TYPE_FLOAT, PROPERTY_HINT_RANGE, "0.0,60.0,0.1")
	_add_project_setting_info(SETTING_F5_ATTACH_CHECK_MAX, TYPE_INT, PROPERTY_HINT_RANGE, "1,100,1")


func _add_project_setting_info(name: String, type: int, hint: int, hint_string: String) -> void:
	ProjectSettings.add_property_info({
		"name": name,
		"type": type,
		"hint": hint,
		"hint_string": hint_string,
	})


func _register_autoload() -> void:
	if ProjectSettings.has_setting("autoload/" + AUTOLOAD_NAME):
		print("[ExternalDebugAttach] Autoload '", AUTOLOAD_NAME, "' already registered")
		return

	ProjectSettings.set_setting("autoload/" + AUTOLOAD_NAME, AUTOLOAD_PATH)
	ProjectSettings.save()
	print("[ExternalDebugAttach] Registered autoload '", AUTOLOAD_NAME, "'")


func _unregister_autoload() -> void:
	if not ProjectSettings.has_setting("autoload/" + AUTOLOAD_NAME):
		return

	ProjectSettings.set_setting("autoload/" + AUTOLOAD_NAME, null)
	ProjectSettings.save()
	print("[ExternalDebugAttach] Unregistered autoload '", AUTOLOAD_NAME, "'")


func _is_auto_register_autoload_enabled() -> bool:
	return ProjectSettings.get_setting(SETTING_AUTO_REGISTER_AUTOLOAD) == true


func _get_ide_type() -> int:
	return int(ProjectSettings.get_setting(SETTING_IDE_TYPE))


func _get_debug_wait_seconds() -> float:
	return float(ProjectSettings.get_setting(SETTING_DEBUG_WAIT_SECONDS))


func _kill_service() -> void:
	if not _is_windows:
		return
	if _service_process_id > 0:
		print("[ExternalDebugAttach] Stopping owned Service PID: ", _service_process_id)
		OS.execute("taskkill", ["/PID", str(_service_process_id), "/F"], [], false, false)
	_service_process_id = -1
	_kill_windows_process_listening_on_port(SERVICE_PORT)
	_service_started_with_console = false


func _kill_windows_process_listening_on_port(port: int) -> void:
	var ps := (
		"Get-NetTCPConnection -LocalPort %d -ErrorAction SilentlyContinue "
		+ "| ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }"
	) % port
	var exit_code := OS.execute("powershell.exe", ["-NoProfile", "-Command", ps], [], false, false)
	if exit_code < 0:
		push_warning("[ExternalDebugAttach] Could not run PowerShell to free port %d (exit cleanup may be incomplete)" % port)


func _get_service_path() -> String:
	var plugin_path := ProjectSettings.globalize_path("res://addons/external_debug_attach/")
	var service_path := plugin_path + "bin/DebugAttachService.exe"

	if FileAccess.file_exists(service_path):
		return service_path

	# Fallback: try development path
	var project_path := ProjectSettings.globalize_path("res://")
	var dev_path := project_path + "/../DebugAttachService/bin/Release/net8.0-windows/DebugAttachService.exe"
	if FileAccess.file_exists(dev_path):
		return dev_path

	return service_path


func _wait_until_service_listening(max_frames: int) -> bool:
	for _i in range(max_frames):
		var tcp := StreamPeerTCP.new()
		if tcp.connect_to_host(SERVICE_HOST, SERVICE_PORT) != OK:
			await get_tree().process_frame
			continue
		for _j in range(10):
			tcp.poll()
			var status := tcp.get_status()
			if status == StreamPeerTCP.STATUS_CONNECTED:
				tcp.disconnect_from_host()
				return true
			if status == StreamPeerTCP.STATUS_ERROR:
				break
			await get_tree().process_frame
		tcp.disconnect_from_host()
		await get_tree().process_frame
	return false


func _ensure_service_running_async() -> bool:
	var show_console: bool = ProjectSettings.get_setting(SETTING_SHOW_SERVICE_CONSOLE)

	# Reuse the process already listening on the port. Do **not** kill and respawn to "switch"
	# to a visible console — that was causing the service (and cmd.exe) to flicker on the taskbar
	# and restart repeatedly when plugin state reset after a script reload.
	if await _wait_until_service_listening(SERVICE_READY_QUICK_FRAMES):
		print(
			"[ExternalDebugAttach] Debug Attach Service already running on port ",
			SERVICE_PORT,
			" (reusing; close DebugAttachService.exe manually if you need a fresh console)."
		)
		return true

	var service_path := _get_service_path()
	if not FileAccess.file_exists(service_path):
		printerr("[ExternalDebugAttach] Service not found at: ", service_path)
		return false

	print("[ExternalDebugAttach] Starting Debug Attach Service (console: ", show_console, ")")

	if show_console:
		var args := ["/c", 'start "DebugAttachService" "' + service_path + '" --port ' + str(SERVICE_PORT)]
		var cmd_pid := OS.create_process("cmd.exe", args)
		if cmd_pid <= 0:
			printerr("[ExternalDebugAttach] Failed to start service (cmd)")
			return false
		# Child is started via `start`; cmd PID is not the listener — stop via port in _kill_service.
		_service_process_id = -1
		_service_started_with_console = true
	else:
		_service_process_id = OS.create_process(service_path, ["--port", str(SERVICE_PORT)])
		_service_started_with_console = false
		if _service_process_id <= 0:
			printerr("[ExternalDebugAttach] Failed to start service")
			return false
		print("[ExternalDebugAttach] Service started with PID: ", _service_process_id)

	if await _wait_until_service_listening(SERVICE_READY_WAIT_FRAMES):
		return true

	printerr("[ExternalDebugAttach] Service did not become ready in time (port ", SERVICE_PORT, ")")
	return false


func _on_button_pressed() -> void:
	print("[ExternalDebugAttach] Button pressed - Run + Attach Debug")
	if _is_auto_register_autoload_enabled():
		_register_autoload()

	if not await _ensure_service_running_async():
		printerr("[ExternalDebugAttach] Cannot run attach: service unavailable")
		return
	await _run_and_attach_main_scene()


func _on_scene_attach_button_pressed() -> void:
	print("[ExternalDebugAttach] Scene attach — pick scene (editor Quick Open UI when available)")
	var ed := get_editor_interface()
	# Same dialog as the built-in “Select Scene” / Ctrl+Alt+O-style quick open (Godot 4.4+).
	if ed.has_method(&"popup_quick_open"):
		ed.popup_quick_open(_on_scene_quick_open_result, [&"PackedScene"])
	else:
		_scene_dialog.popup_centered_ratio(0.75)


## Called by EditorInterface.popup_quick_open (cancel passes "").
func _on_scene_quick_open_result(path: String) -> void:
	if path.is_empty():
		return
	# Engine invokes this synchronously; defer so async attach can run safely.
	call_deferred(&"_prepare_and_attach_scene_async", path)


func _prepare_and_attach_scene_async(scene_path: String) -> void:
	await _prepare_and_attach_scene(scene_path)


func _on_scene_selected_for_attach(scene_path: String) -> void:
	await _prepare_and_attach_scene(scene_path)


func _prepare_and_attach_scene(scene_path: String) -> void:
	print("[ExternalDebugAttach] Scene chosen: ", scene_path)
	if not scene_path.ends_with(".tscn"):
		push_warning("[ExternalDebugAttach] Expected a .tscn file.")
	if not ResourceLoader.exists(scene_path):
		printerr("[ExternalDebugAttach] Scene not found: ", scene_path)
		return

	if _is_auto_register_autoload_enabled():
		_register_autoload()

	if not await _ensure_service_running_async():
		printerr("[ExternalDebugAttach] Cannot run attach: service unavailable")
		return

	await _run_and_attach_with_scene_path(scene_path)


func _run_and_attach_main_scene() -> void:
	await _run_and_attach_with_scene_path("")


## If scene_path is empty, runs the main scene; otherwise runs that scene file via EditorInterface.play_custom_scene.
func _run_and_attach_with_scene_path(scene_path: String) -> void:
	var ed := get_editor_interface()
	if ed.has_method(&"is_playing") and ed.is_playing():
		print("[ExternalDebugAttach] Stopping previous run (avoids multiple game windows on the taskbar).")
		ed.stop_playing_scene()
		var guard := 0
		while ed.is_playing() and guard < 180:
			await get_tree().process_frame
			guard += 1
		await get_tree().create_timer(0.2).timeout

	if scene_path.is_empty():
		ed.play_main_scene()
		print("[ExternalDebugAttach] Main scene started")
	else:
		ed.play_custom_scene(scene_path)
		print("[ExternalDebugAttach] Custom scene started: ", scene_path)

	await _notify_service_async()


func _notify_service_async() -> void:
	print("[ExternalDebugAttach] Notifying Debug Attach Service...")

	var tcp := StreamPeerTCP.new()
	var err := tcp.connect_to_host(SERVICE_HOST, SERVICE_PORT)
	if err != OK:
		printerr("[ExternalDebugAttach] Failed to connect to service: ", err)
		return

	# Wait for connection
	for i in range(20):
		tcp.poll()
		if tcp.get_status() == StreamPeerTCP.STATUS_CONNECTED:
			break
		elif tcp.get_status() == StreamPeerTCP.STATUS_ERROR:
			printerr("[ExternalDebugAttach] Connection error")
			return
		await get_tree().process_frame

	if tcp.get_status() != StreamPeerTCP.STATUS_CONNECTED:
		printerr("[ExternalDebugAttach] Connection timeout")
		tcp.disconnect_from_host()
		return

	# Send minimal request - Service handles IDE detection, PID detection, attach
	var ide_type := _get_ide_type()
	var editor := "vscode"
	match ide_type:
		IdeType.Cursor:
			editor = "cursor"
		IdeType.AntiGravity:
			editor = "antigravity"

	var f5_max: int = int(ProjectSettings.get_setting(SETTING_F5_ATTACH_CHECK_MAX))
	if f5_max < 1:
		f5_max = 1
	elif f5_max > 100:
		f5_max = 100

	var request := {
		"type": "debug-attach-request",
		"pid": 0,
		"engine": "godot",
		"editor": editor,
		"workspacePath": ProjectSettings.globalize_path("res://"),
		"idePath": _get_configured_ide_path(ide_type),
		"f5AttachCheckMax": f5_max
	}

	var json := JSON.stringify(request)
	tcp.put_data((json + "\n").to_utf8_buffer())
	print("[ExternalDebugAttach] Request sent: ", editor)

	# Wait for response
	for i in range(200):
		tcp.poll()
		if tcp.get_available_bytes() > 0:
			print("[ExternalDebugAttach] Response: ", tcp.get_utf8_string(tcp.get_available_bytes()))
			break
		await get_tree().process_frame

	tcp.disconnect_from_host()


func _get_configured_ide_path(ide_type: int) -> String:
	match ide_type:
		IdeType.Cursor:
			return str(ProjectSettings.get_setting(SETTING_CURSOR_PATH))
		IdeType.AntiGravity:
			return str(ProjectSettings.get_setting(SETTING_ANTIGRAVITY_PATH))
		_:
			return str(ProjectSettings.get_setting(SETTING_VSCODE_PATH))
