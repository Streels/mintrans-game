extends Node
## Художественная проверка ассета в движке: GLB, спрайты и движущийся состав.

const ConsistView = preload("res://scripts/Art/Ed4mConsistView.gd")
var _consist: Node2D
var _sprite_stage: Node2D
var _viewport_container: SubViewportContainer
var _camera: Camera3D
var _model: Node3D
var _kind: String = "head"
var _mode: String = "3d"
var _yaw: float = -0.85
var _zoom: float = 17.0
var _rotating: bool = false
var _label: Label
var _screenshot: String = ""
var _frames: int = 0

func _ready() -> void:
	RenderingServer.set_default_clear_color(Color("202821"))
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--screenshot="):
			_screenshot = arg.trim_prefix("--screenshot=")
		if arg == "--mode=sprites":
			_mode = "sprites"
	_create_model_view()
	_create_sprite_view()
	_create_ui()
	_load_model("head")
	_set_mode(_mode)

func _create_model_view() -> void:
	_viewport_container = SubViewportContainer.new()
	_viewport_container.position = Vector2(300, 105)
	_viewport_container.size = Vector2(1260, 680)
	_viewport_container.stretch = true
	add_child(_viewport_container)
	var viewport := SubViewport.new()
	viewport.size = Vector2i(1260, 680)
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	viewport.own_world_3d = true
	_viewport_container.add_child(viewport)
	var world := Node3D.new()
	viewport.add_child(world)
	var environment := WorldEnvironment.new()
	var settings := Environment.new()
	settings.background_mode = Environment.BG_COLOR
	settings.background_color = Color("252d27")
	settings.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	settings.ambient_light_color = Color("d2d5c6")
	settings.ambient_light_energy = 0.35
	settings.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	environment.environment = settings
	world.add_child(environment)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-48, -40, 0)
	light.light_energy = 0.8
	light.shadow_enabled = true
	world.add_child(light)
	_model = Node3D.new()
	world.add_child(_model)
	_camera = Camera3D.new()
	_camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	_camera.near = 1.0
	_camera.far = 150.0
	_camera.size = _zoom
	world.add_child(_camera)
	_update_camera()

func _create_sprite_view() -> void:
	_sprite_stage = Node2D.new()
	_sprite_stage.position = Vector2(945, 440)
	add_child(_sprite_stage)
	_consist = ConsistView.new()
	_consist.tile_width = 104.0
	_consist.tile_height = 52.0
	_consist.speed = 0.65
	_sprite_stage.add_child(_consist)
	_consist.set_route(PackedVector2Array([Vector2(-3,-5),Vector2(3,-5),Vector2(5,-3),Vector2(5,3),Vector2(3,5),Vector2(-3,5),Vector2(-5,3),Vector2(-5,-3),Vector2(-3,-5)]))

func _label_at(text: String, position: Vector2, size: int, color: Color, parent: Node) -> Label:
	var label := Label.new()
	label.text = text
	label.position = position
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", color)
	parent.add_child(label)
	return label

func _button(text: String, position: Vector2, action: Callable, parent: Node) -> void:
	var button := Button.new()
	button.text = text
	button.position = position
	button.size = Vector2(228, 44)
	button.add_theme_font_size_override("font_size", 17)
	button.pressed.connect(action)
	parent.add_child(button)

func _create_ui() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	var ui := Control.new()
	layer.add_child(ui)
	_label_at("МИНТРАНС  /  МАСТЕРСКАЯ ТЕХНИКИ", Vector2(32, 22), 16, Color("a4b19c"), ui)
	_label_at("ЭД4М", Vector2(30, 51), 44, Color("e4ddc7"), ui)
	_label_at("ПЕРВЫЙ ИГРОВОЙ АССЕТ", Vector2(32, 116), 13, Color("a4b19c"), ui)
	_button("Головной вагон", Vector2(32, 165), func(): _load_model("head"), ui)
	_button("Моторный вагон", Vector2(32, 217), func(): _load_model("motor"), ui)
	_button("Прицепной вагон", Vector2(32, 269), func(): _load_model("trailer"), ui)
	_label_at("ПРОВЕРКА В ДВИЖКЕ", Vector2(32, 355), 13, Color("a4b19c"), ui)
	_button("Редактируемая 3D-модель", Vector2(32, 391), func(): _set_mode("3d"), ui)
	_button("Состав · 2D-спрайты", Vector2(32, 443), func(): _set_mode("sprites"), ui)
	_label_at("Зелёно-кремовая окраска\n8 направлений · 3 модуля\nИзометрия 2:1", Vector2(32, 535), 17, Color("c6c8b8"), ui)
	_label_at("Мышь с зажатой ЛКМ — поворот\nКолесо — масштаб\nПробел — пауза состава", Vector2(32, 665), 15, Color("9da795"), ui)
	_label = _label_at("", Vector2(336, 52), 21, Color("e4ddc7"), ui)
	_label_at("Художественная сцена просмотра • исходник .blend + модули .glb + прозрачные спрайты", Vector2(32, 839), 16, Color("9da795"), ui)

func _load_model(kind: String) -> void:
	_kind = kind
	for child in _model.get_children():
		_model.remove_child(child)
		child.queue_free()
	var scene: PackedScene = load("res://assets/vehicles/ed4m/models/ed4m_" + kind + ".glb")
	_model.add_child(scene.instantiate())
	if is_instance_valid(_label):
		_label.text = {"head":"Головной вагон · оригинальная кабина ЭД4М", "motor":"Моторный вагон · ромбический пантограф", "trailer":"Прицепной вагон · промежуточный модуль"}[kind]

func _set_mode(mode: String) -> void:
	_mode = mode
	_viewport_container.visible = mode == "3d"
	_sprite_stage.visible = mode == "sprites"
	if mode == "sprites":
		_label.text = "Четырёхвагонный состав · движение по восьми направлениям"
	else:
		_load_model(_kind)

func _update_camera() -> void:
	_camera.size = _zoom
	_camera.position = Vector3(sin(_yaw) * 34, 19, cos(_yaw) * 34)
	_camera.look_at(Vector3(0, 2.0, 0), Vector3.UP)

func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT:
			_rotating = event.pressed and event.position.x > 300
		if event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_zoom = maxf(12.0, _zoom - 1.5)
			_update_camera()
		if event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_zoom = minf(45.0, _zoom + 1.5)
			_update_camera()
	if event is InputEventMouseMotion and _rotating:
		_yaw += event.relative.x * 0.008
		_update_camera()
	if event is InputEventKey and event.pressed and event.keycode == KEY_SPACE:
		_consist.paused = not _consist.paused

func _process(_delta: float) -> void:
	if not _screenshot.is_empty():
		_frames += 1
		if _frames == 45:
			var error := get_viewport().get_texture().get_image().save_png(_screenshot)
			print("ED4M screenshot: ", error)
			get_tree().quit(0 if error == OK else 1)
