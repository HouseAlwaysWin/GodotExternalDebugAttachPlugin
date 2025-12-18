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

const AUTOLOAD_NAME := "DebugWait"
const AUTOLOAD_PATH := "res://addons/external_debug_attach/DebugWaitAutoload.cs"

enum IdeType {VSCode, Cursor, AntiGravity}

var _button: Button
var _editor_settings: EditorSettings
var _tcp_client: StreamPeerTCP
var _service_process_id: int = -1

func _enter_tree() -> void:
	print("[ExternalDebugAttach] GDScript plugin loaded")
	
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
	
	# Register DebugWait Autoload
	_register_autoload()
	
	print("[ExternalDebugAttach] Ready with shortcut Alt+F5")

func _exit_tree() -> void:
	print("[ExternalDebugAttach] Unloading...")
	
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
	
	# Unregister Autoload
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

func _get_ide_type() -> IdeType:
	return _editor_settings.get_setting(SETTING_IDE_TYPE) as IdeType

func _kill_service() -> void:
	# Kill any running DebugAttachService process
	print("[ExternalDebugAttach] Killing any running Service instances...")
	OS.execute("taskkill", ["/IM", "DebugAttachService.exe", "/F"], [], false)

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

func _is_service_running() -> bool:
	var tcp := StreamPeerTCP.new()
	var err := tcp.connect_to_host(SERVICE_HOST, SERVICE_PORT)
	if err != OK:
		return false
	
	# Wait for connection
	for i in range(10):
		tcp.poll()
		if tcp.get_status() == StreamPeerTCP.STATUS_CONNECTED:
			tcp.disconnect_from_host()
			return true
		elif tcp.get_status() == StreamPeerTCP.STATUS_ERROR:
			return false
		await get_tree().process_frame
	
	tcp.disconnect_from_host()
	return false

func _ensure_service_running() -> void:
	# Check if service is already running - use a simple async approach
	var tcp := StreamPeerTCP.new()
	tcp.connect_to_host(SERVICE_HOST, SERVICE_PORT)
	
	# Quick poll to check connection
	for i in range(5):
		tcp.poll()
		if tcp.get_status() == StreamPeerTCP.STATUS_CONNECTED:
			print("[ExternalDebugAttach] Debug Attach Service is already running")
			tcp.disconnect_from_host()
			return
		elif tcp.get_status() == StreamPeerTCP.STATUS_ERROR:
			break
		# Small delay using OS
		OS.delay_msec(50)
	
	tcp.disconnect_from_host()
	
	# Service not running, start it
	var service_path := _get_service_path()
	if not FileAccess.file_exists(service_path):
		printerr("[ExternalDebugAttach] Service not found at: ", service_path)
		return
	
	var show_console: bool = _editor_settings.get_setting(SETTING_SHOW_SERVICE_CONSOLE)
	print("[ExternalDebugAttach] Starting Debug Attach Service (console: ", show_console, ")")
	
	if show_console:
		# Use cmd /c start to open a visible console window
		var args := ["/c", 'start "DebugAttachService" "' + service_path + '" --port ' + str(SERVICE_PORT)]
		_service_process_id = OS.create_process("cmd.exe", args)
	else:
		# Start in background (no visible window)
		_service_process_id = OS.create_process(service_path, ["--port", str(SERVICE_PORT)])
	
	if _service_process_id > 0:
		print("[ExternalDebugAttach] Service started with PID: ", _service_process_id)
	else:
		printerr("[ExternalDebugAttach] Failed to start service")

func _on_button_pressed() -> void:
	print("[ExternalDebugAttach] Button pressed - Run + Attach Debug")
	_run_and_attach()

func _run_and_attach() -> void:
	# Step 1: Run the project
	EditorInterface.play_main_scene()
	print("[ExternalDebugAttach] Project started")
	
	# Step 2: Notify Service to do the attach (Service handles everything)
	_notify_service()

func _notify_service() -> void:
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
		OS.delay_msec(50)
	
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
		"workspacePath": ProjectSettings.globalize_path("res://")
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
		OS.delay_msec(50)
	
	tcp.disconnect_from_host()
