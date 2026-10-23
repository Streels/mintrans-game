class_name Ed4mConsistView
extends Node2D
## Отображение ЭД4М: только графика, без пассажирской и транспортной симуляции.
## route содержит координаты сетки, как IsoProjection (x-y, x+y).

const ASSET_ROOT := "res://assets/vehicles/ed4m/"
const DIRECTIONS := [Vector2(0, 1), Vector2(-1, 1), Vector2(-1, 0), Vector2(-1, -1), Vector2(0, -1), Vector2(1, -1), Vector2(1, 0), Vector2(1, 1)]
const DIRECTION_NAMES := ["s", "sw", "w", "nw", "n", "ne", "e", "se"]

@export var tile_width: float = 128.0
@export var tile_height: float = 64.0
@export var car_pitch: float = 1.0
@export var speed: float = 0.7
@export var paused: bool = false
@export var show_route: bool = true

var route := PackedVector2Array()
var distance: float = 5.0
var cars := ["head", "motor", "trailer", "head"]
var _textures: Dictionary = {}
var _metadata: Dictionary = {}
var _total_length: float = 0.0

func _ready() -> void:
	_metadata = JSON.parse_string(FileAccess.get_file_as_string(ASSET_ROOT + "ed4m.json"))
	for kind in ["head", "motor", "trailer"]:
		for direction in DIRECTION_NAMES:
			_textures[kind + "/" + direction] = load(ASSET_ROOT + "sprites/ed4m_" + kind + "_" + direction + ".png")
	texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR

func set_route(points: PackedVector2Array) -> void:
	route = points
	_total_length = 0.0
	for index in range(1, route.size()):
		_total_length += route[index - 1].distance_to(route[index])
	queue_redraw()

func project(point: Vector2) -> Vector2:
	return Vector2((point.x - point.y) * tile_width / 2.0, (point.x + point.y) * tile_height / 2.0)

func sample_path(along: float) -> Dictionary:
	if route.size() < 2 or _total_length <= 0.0:
		return {"position": Vector2.ZERO, "direction": Vector2(0, 1)}
	var remaining := fposmod(along, _total_length)
	for index in range(1, route.size()):
		var segment := route[index] - route[index - 1]
		var length := segment.length()
		if length <= 0.00001:
			continue
		if remaining <= length:
			return {"position": route[index - 1] + segment * remaining / length, "direction": segment.normalized()}
		remaining -= length
	return {"position": route[-1], "direction": (route[-1] - route[-2]).normalized()}

func _process(delta: float) -> void:
	if not paused:
		distance += speed * delta
	queue_redraw()

func _draw() -> void:
	if _metadata.is_empty():
		return
	if show_route and route.size() > 1:
		var projected := PackedVector2Array()
		for point in route:
			projected.append(project(point))
		draw_polyline(projected, Color("484e45"), 7.0, true)
		draw_polyline(projected, Color("7d8074"), 1.0, true)
	var draw_list: Array[Dictionary] = []
	for index in range(cars.size()):
		var sample := sample_path(distance - index * car_pitch)
		var direction: Vector2 = sample.direction
		if index == cars.size() - 1 and cars[index] == "head":
			direction = -direction
		var best: int = 0
		var best_dot: float = -2.0
		for heading in range(DIRECTIONS.size()):
			var dot: float = direction.dot(DIRECTIONS[heading].normalized())
			if dot > best_dot:
				best_dot = dot
				best = heading
		draw_list.append({"position": project(sample.position), "key": cars[index] + "/" + DIRECTION_NAMES[best]})
	draw_list.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return a.position.y < b.position.y)
	var px_per_m: float = _metadata.pixels_per_metre
	var pitch_m: float = _metadata.coupling_pitch_m
	var sprite_scale: float = tile_width / 2.0 / (pitch_m * sqrt(0.5) * px_per_m) * car_pitch
	var pivot_data: Array = _metadata.pivot_px
	var pivot := Vector2(pivot_data[0], pivot_data[1]) * sprite_scale
	for item in draw_list:
		var texture: Texture2D = _textures[item.key]
		draw_texture_rect(texture, Rect2(item.position - pivot, texture.get_size() * sprite_scale), false)
