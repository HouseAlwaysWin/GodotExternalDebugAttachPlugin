@tool
extends Node

## GDScript version of DebugWait Autoload
## For GDScript projects, this provides a configurable startup delay
## to allow time for the debugger to be ready

## Maximum time to wait (in seconds)
@export var max_wait_seconds: float = 5.0

var _wait_label: Label
var _start_time: float

func _ready() -> void:
	# Only wait if running from editor (debug mode)
	if not OS.is_debug_build():
		print("[DebugWait] Release build - skipping wait")
		return
	
	# For GDScript, we just provide a short delay for debugger readiness
	print("[DebugWait] Waiting for debugger to be ready...")
	print("[DebugWait] (Press ESC in game window to skip)")
	
	# Show a visual indicator
	_create_wait_overlay()
	_start_time = Time.get_unix_time_from_system()
	
	# Use a timer instead of blocking
	set_process(true)

func _process(delta: float) -> void:
	var elapsed := Time.get_unix_time_from_system() - _start_time
	
	# Check for timeout
	if elapsed >= max_wait_seconds:
		print("[DebugWait] Wait complete - resuming game")
		_cleanup()
		return
	
	# Update the wait label
	if _wait_label:
		var remaining := max_wait_seconds - elapsed
		_wait_label.text = "Waiting for debugger... (%.1fs)\nPress ESC to skip" % remaining
	
	# Allow user to skip by pressing ESC
	if Input.is_action_just_pressed("ui_cancel"):
		print("[DebugWait] User skipped wait")
		_cleanup()

func _create_wait_overlay() -> void:
	# Create a simple overlay to indicate waiting
	var overlay := ColorRect.new()
	overlay.color = Color(0, 0, 0, 0.7)
	overlay.anchors_preset = Control.PRESET_FULL_RECT
	
	_wait_label = Label.new()
	_wait_label.text = "Waiting for debugger..."
	_wait_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_wait_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_wait_label.anchors_preset = Control.PRESET_FULL_RECT
	_wait_label.add_theme_color_override("font_color", Color.WHITE)
	_wait_label.add_theme_font_size_override("font_size", 24)
	
	var canvas_layer := CanvasLayer.new()
	canvas_layer.layer = 100
	canvas_layer.add_child(overlay)
	canvas_layer.add_child(_wait_label)
	add_child(canvas_layer)

func _cleanup() -> void:
	set_process(false)
	
	# Remove overlay
	for child in get_children():
		if child is CanvasLayer:
			child.queue_free()
	
	print("[DebugWait] Game resumed")
