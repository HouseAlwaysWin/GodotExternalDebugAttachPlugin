@tool
extends EditorPlugin

## Pure GDScript External Debug Attach Plugin
## Avoids C# assembly reload issues by using GDScript only
## Communicates with Debug Attach Service via TCP

const SERVICE_PORT := 47632
const SERVICE_HOST := "127.0.0.1"
const SETTING_PREFIX := "external_debug_attach/"
const SETTING_IDE_TYPE := SETTING_PREFIX + "ide_type"
const SETTING_VSCODE_PATH := SETTING_PREFIX + "vscode_path"
const SETTING_CURSOR_PATH := SETTING_PREFIX + "cursor_path"
const SETTING_ANTIGRAVITY_PATH := SETTING_PREFIX + "antigravity_path"
const SETTING_SHOW_SERVICE_CONSOLE := SETTING_PREFIX + "show_service_console"
const SETTING_AUTO_REGISTER_AUTOLOAD := SETTING_PREFIX + "auto_register_debugwait_autoload"
const SETTING_DEBUG_WAIT_SECONDS := SETTING_PREFIX + "debug_wait_seconds"

const AUTOLOAD_NAME := "DebugWait"
const AUTOLOAD_PATH := "res://addons/external_debug_attach/DebugWaitAutoload.gd"
const SERVICE_READY_QUICK_FRAMES := 30
const SERVICE_READY_WAIT_FRAMES := 300

enum IdeType {VSCode, Cursor, AntiGravity}

var _button: Button
var _editor_settings: EditorSettings
var _tcp_client: StreamPeerTCP
var _service_process_id: int = -1
var _is_windows: bool = OS.get_name() == "Windows"

func _enter_tree() -> void:
	print("[ExternalDebugAttach] GDScript plugin loaded")
	if not _is_windows:
		push_warning("[ExternalDebugAttach] This plugin currently supports Windows only.")
		return
	
	_editor_settings = EditorInterface.get_editor_settings()
	_initialize_settings()
	
	# Create button
	_button = Button.new()
	_button.tooltip_text = "Run + Attach Debug (Alt+F5)"
	_button.pressed.connect(_on_button_pressed)
	
	# Load icon
	var icon = load("res://addons/external_debug_attach/attach_icon.svg")
	if icon:
		_button.icon = icon
	else:
		_button.text = "▶ Attach"
	
	add_control_to_container(CONTAINER_TOOLBAR, _button)
	
	# Setup shortcut (Alt+F5)
	var shortcut = Shortcut.new()
	var input_event = InputEventKey.new()
	input_event.keycode = KEY_F5
	input_event.alt_pressed = true
	shortcut.events = [input_event]
	_button.shortcut = shortcut
	_button.shortcut_in_tooltip = true
	
	# Ensure service is running
	_ensure_service_running()
	
	# Register DebugWait Autoload only when enabled
	if _is_auto_register_autoload_enabled():
		_register_autoload()
	else:
		_unregister_autoload()
	
	print("[ExternalDebugAttach] Ready with shortcut Alt+F5")

func _exit_tree() -> void:
	print("[ExternalDebugAttach] Unloading...")
	if not _is_windows:
		return
	
	if _button:
		_button.pressed.disconnect(_on_button_pressed)
		remove_control_from_container(CONTAINER_TOOLBAR, _button)
		_button.queue_free()
		_button = null
	
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
	# IDE Type dropdown
	if not _editor_settings.has_setting(SETTING_IDE_TYPE):
		_editor_settings.set_setting(SETTING_IDE_TYPE, IdeType.VSCode)
	_add_setting_info(SETTING_IDE_TYPE, TYPE_INT, PROPERTY_HINT_ENUM, "VSCode,Cursor,AntiGravity")
	
	# VS Code Path
	if not _editor_settings.has_setting(SETTING_VSCODE_PATH):
		_editor_settings.set_setting(SETTING_VSCODE_PATH, "")
	_add_setting_info(SETTING_VSCODE_PATH, TYPE_STRING, PROPERTY_HINT_GLOBAL_FILE, "*.exe")
	
	# Cursor Path
	if not _editor_settings.has_setting(SETTING_CURSOR_PATH):
		_editor_settings.set_setting(SETTING_CURSOR_PATH, "")
	_add_setting_info(SETTING_CURSOR_PATH, TYPE_STRING, PROPERTY_HINT_GLOBAL_FILE, "*.exe")
	
	# AntiGravity Path
	if not _editor_settings.has_setting(SETTING_ANTIGRAVITY_PATH):
		_editor_settings.set_setting(SETTING_ANTIGRAVITY_PATH, "")
	_add_setting_info(SETTING_ANTIGRAVITY_PATH, TYPE_STRING, PROPERTY_HINT_GLOBAL_FILE, "*.exe")
	
	# Show Service Console (for debugging)
	if not _editor_settings.has_setting(SETTING_SHOW_SERVICE_CONSOLE):
		_editor_settings.set_setting(SETTING_SHOW_SERVICE_CONSOLE, false)
	_add_setting_info(SETTING_SHOW_SERVICE_CONSOLE, TYPE_BOOL, PROPERTY_HINT_NONE, "")

	# Auto register DebugWait autoload
	if not _editor_settings.has_setting(SETTING_AUTO_REGISTER_AUTOLOAD):
		_editor_settings.set_setting(SETTING_AUTO_REGISTER_AUTOLOAD, true)
	_add_setting_info(SETTING_AUTO_REGISTER_AUTOLOAD, TYPE_BOOL, PROPERTY_HINT_NONE, "")

	# Debug wait seconds shown in countdown overlay
	if not _editor_settings.has_setting(SETTING_DEBUG_WAIT_SECONDS):
		_editor_settings.set_setting(SETTING_DEBUG_WAIT_SECONDS, 5.0)
	_add_setting_info(SETTING_DEBUG_WAIT_SECONDS, TYPE_FLOAT, PROPERTY_HINT_RANGE, "0.0,30.0,0.1")

func _add_setting_info(name: String, type: int, hint: int, hint_string: String) -> void:
	_editor_settings.add_property_info({
		"name": name,
		"type": type,
		"hint": hint,
		"hint_string": hint_string
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
	return _editor_settings.get_setting(SETTING_AUTO_REGISTER_AUTOLOAD) == true

func _get_ide_type() -> int:
	return int(_editor_settings.get_setting(SETTING_IDE_TYPE))

func _get_debug_wait_seconds() -> float:
	return float(_editor_settings.get_setting(SETTING_DEBUG_WAIT_SECONDS))

func _sync_debugwait_settings() -> void:
	ProjectSettings.set_setting("external_debug_attach/debug_wait_seconds", _get_debug_wait_seconds())
	ProjectSettings.save()

func _kill_service() -> void:
	if _service_process_id <= 0:
		print("[ExternalDebugAttach] No owned Service PID to stop")
		return
	print("[ExternalDebugAttach] Stopping owned Service PID: ", _service_process_id)
	OS.execute("taskkill", ["/PID", str(_service_process_id), "/F"], [], false)
	_service_process_id = -1

func _get_service_path() -> String:
	var plugin_path := ProjectSettings.globalize_path("res://addons/external_debug_attach/")
	var service_path := plugin_path + "bin/DebugAttachService.exe"
	
	if FileAccess.file_exists(service_path):
		return service_path
	
	# Fallback: try development path
	var project_path := ProjectSettings.globalize_path("res://")
	var dev_path := project_path + "/../DebugAttachService/bin/Release/net8.0/DebugAttachService.exe"
	if FileAccess.file_exists(dev_path):
		return dev_path
	
	return service_path

func _ensure_service_running() -> void:
	_sync_debugwait_settings()
	_ensure_service_running_async()

## Returns true when TCP accepts connections on SERVICE_PORT (service is listening).
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
	_sync_debugwait_settings()
	if await _wait_until_service_listening(SERVICE_READY_QUICK_FRAMES):
		print("[ExternalDebugAttach] Debug Attach Service is already running")
		return true

	var service_path := _get_service_path()
	if not FileAccess.file_exists(service_path):
		printerr("[ExternalDebugAttach] Service not found at: ", service_path)
		return false

	var show_console: bool = _editor_settings.get_setting(SETTING_SHOW_SERVICE_CONSOLE)
	print("[ExternalDebugAttach] Starting Debug Attach Service (console: ", show_console, ")")

	if show_console:
		var args := ["/c", 'start "DebugAttachService" "' + service_path + '" --port ' + str(SERVICE_PORT)]
		_service_process_id = OS.create_process("cmd.exe", args)
	else:
		_service_process_id = OS.create_process(service_path, ["--port", str(SERVICE_PORT)])

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
	if not await _ensure_service_running_async():
		printerr("[ExternalDebugAttach] Cannot run attach: service unavailable")
		return
	_run_and_attach()

func _run_and_attach() -> void:
	# Always sync latest wait seconds before launching game.
	_sync_debugwait_settings()

	# Step 1: Run the project
	EditorInterface.play_main_scene()
	print("[ExternalDebugAttach] Project started")
	
	# Step 2: Notify Service to do the attach (Service handles everything)
	_notify_service_async()

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
	
	var request := {
		"type": "debug-attach-request",
		"pid": 0,
		"engine": "godot",
		"editor": editor,
		"workspacePath": ProjectSettings.globalize_path("res://"),
		"idePath": _get_configured_ide_path(ide_type)
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
			return str(_editor_settings.get_setting(SETTING_CURSOR_PATH))
		IdeType.AntiGravity:
			return str(_editor_settings.get_setting(SETTING_ANTIGRAVITY_PATH))
		_:
			return str(_editor_settings.get_setting(SETTING_VSCODE_PATH))
