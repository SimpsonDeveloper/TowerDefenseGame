shader_type canvas_item;

// A swirly, pulsing black/white portal drawn procedurally — no texture needed.
// Designed for a MeshInstance2D + QuadMesh (UVs 0..1, centered). The dark
// interior is opaque (reads as a hole), white spiral arms wind into it, and a
// bright ring marks the "event horizon". `intensity` is driven from script so
// the portal can fade in and flare when a wave fires.

// --- Shape ---
uniform float radius : hint_range(0.1, 1.0) = 0.95;        // outer edge, in UV-half units (1.0 = quad edge)
uniform float edge_softness : hint_range(0.001, 0.5) = 0.18;
uniform float core_size : hint_range(0.0, 0.6) = 0.16;     // radius of the dark central sink
uniform float core_softness : hint_range(0.001, 0.5) = 0.1;

// --- Spiral ---
uniform float arm_count : hint_range(1.0, 12.0) = 5.0;
uniform float twist : hint_range(0.0, 12.0) = 5.0;         // log-spiral tightness (how hard arms curl in)
uniform float swirl_speed : hint_range(-3.0, 3.0) = 0.8;   // rotations per second (negative = reverse)
uniform float contrast : hint_range(1.0, 12.0) = 3.5;      // black<->white crispness of the arms

// --- Pulse ---
uniform float pulse_speed : hint_range(0.0, 6.0) = 1.6;
uniform float pulse_depth : hint_range(0.0, 1.0) = 0.3;    // how much the brightness "breathes"

// --- Look ---
uniform vec3 bright_color : source_color = vec3(1.0);
uniform vec3 dark_color : source_color = vec3(0.0);
uniform float intensity : hint_range(0.0, 2.0) = 1.0;      // overall energy; <=0 hides the portal
uniform float time_offset = 0.0;                            // per-instance phase so portals don't sync

void fragment() {
	vec2 p = (UV - vec2(0.5)) * 2.0;   // -1..1 across the quad
	float r = length(p);
	float a = atan(p.y, p.x);
	float t = TIME + time_offset;

	// Logarithmic spiral: arms curl toward the center and rotate over time.
	float rr = max(r, 0.04);           // clamp away from the log singularity at r=0
	float spiral = sin(arm_count * a + twist * log(rr) - swirl_speed * t * 6.28318);

	// A fainter counter-rotating set of arms interleaves for depth.
	float spiral_b = sin(arm_count * a - twist * 1.7 * log(rr) + swirl_speed * 0.6 * t * 6.28318);

	// Crisp black/white separation via gamma on the 0..1 band.
	float band = pow(0.5 + 0.5 * spiral, contrast);
	float band_b = pow(0.5 + 0.5 * spiral_b, contrast) * 0.6;
	band = max(band, band_b);

	// Pulse — the whole thing breathes in brightness.
	float pulse = 1.0 - pulse_depth * (0.5 + 0.5 * sin(t * pulse_speed * 6.28318));

	// Carve out the dark central sink; arms fade as they reach it.
	float core_mask = smoothstep(core_size, core_size + core_softness, r);
	band *= core_mask;

	// Bright ring right at the event horizon (Gaussian falloff).
	float ring = exp(-pow((r - core_size) / 0.05, 2.0));

	float lum = clamp(band + ring, 0.0, 1.0) * pulse;
	vec3 col = mix(dark_color, bright_color, lum);

	// Circular outer falloff -> alpha. Interior stays opaque (it's a hole),
	// so alpha comes from the disc shape, not the luminance.
	float outer = 1.0 - smoothstep(radius - edge_softness, radius, r);

	COLOR = vec4(col, outer * clamp(intensity, 0.0, 1.0));
}