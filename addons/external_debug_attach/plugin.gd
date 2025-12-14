@tool
extends EditorPlugin

## GDScript wrapper for External Debug Attach button
## This avoids C# delegate invalidation issues during .NET assembly reload

var _button: Button
var _cs_plugin: Object  # Reference to the C# plugin logic

func _enter_tree() -> void:
	print("[ExternalDebugAttach] GDScript wrapper loaded")
	
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
	
	# Create C# plugin logic instance
	_create_cs_plugin()
	
	print("[ExternalDebugAttach] Ready with shortcut Alt+F5")

func _create_cs_plugin() -> void:
	# Load and instantiate the C# logic class
	var script = load("res://addons/external_debug_attach/ExternalDebugAttachLogic.cs")
	if script:
		_cs_plugin = script.new()
		if _cs_plugin.has_method("Initialize"):
			_cs_plugin.Initialize()
		print("[ExternalDebugAttach] C# logic initialized")
	else:
		printerr("[ExternalDebugAttach] Failed to load C# logic script")

func _exit_tree() -> void:
	print("[ExternalDebugAttach] Unloading...")
	
	if _button:
		_button.pressed.disconnect(_on_button_pressed)
		remove_control_from_container(CONTAINER_TOOLBAR, _button)
		_button.queue_free()
		_button = null
	
	if _cs_plugin:
		if _cs_plugin.has_method("Cleanup"):
			_cs_plugin.Cleanup()
		_cs_plugin = null
	
	print("[ExternalDebugAttach] Unloaded")

func _on_button_pressed() -> void:
	print("[ExternalDebugAttach] Button pressed")
	
	# Recreate C# plugin if it was invalidated by assembly reload
	if _cs_plugin == null or not is_instance_valid(_cs_plugin):
		print("[ExternalDebugAttach] Recreating C# logic after assembly reload...")
		_create_cs_plugin()
	
	if _cs_plugin and _cs_plugin.has_method("RunAndAttach"):
		_cs_plugin.RunAndAttach()
	else:
		printerr("[ExternalDebugAttach] C# logic not available")
