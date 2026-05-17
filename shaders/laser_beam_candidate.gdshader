shader_type canvas_item;

// How hot the beam is. 0 = no core, just the base texture (faded at edges).
// ~0.2+ = a thin white core appears. Higher = thicker, brighter core.
uniform float intensity : hint_range(0.0, 2.0) = 1.0;

// Softness of the white core's falloff into the base color.
uniform float core_softness : hint_range(0.001, 0.5) = 0.12;

// How much of the top/bottom is alpha-faded.
uniform float edge_softness : hint_range(0.0, 1.0) = 0.35;

// Below this intensity value, there is no visible core at all.
uniform float core_floor : hint_range(0.0, 1.0) = 0.2;

void fragment() {
    vec4 base = texture(TEXTURE, UV);

    // 0 at the horizontal equator, 1 at the top/bottom edges.
    float d = abs(UV.y - 0.5) * 2.0;

    // Core half-width grows with intensity past the floor.
    float core_size = max(0.0, intensity - core_floor) * 0.5;

    // White-hot mask: 1 inside core_size, smoothly falling to 0.
    float core = 1.0 - smoothstep(core_size, core_size + core_softness, d);
    core *= clamp(intensity, 0.0, 1.0);

    vec3 col = mix(base.rgb, vec3(1.0), core);

    float edge_fade = 1.0 - smoothstep(1.0 - edge_softness, 1.0, d);
    float alpha = base.a * edge_fade;

    COLOR = vec4(col, alpha);
}
