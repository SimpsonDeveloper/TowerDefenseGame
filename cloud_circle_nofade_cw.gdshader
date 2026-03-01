shader_type canvas_item;

// How fast the clouds animate and orbit
uniform float speed : hint_range(0.0, 2.0) = 0.3;
// How tightly the inner regions orbit relative to outer (higher = more spiral)
uniform float swirl_strength : hint_range(1.0, 12.0) = 4.0;
// Noise scale — higher means smaller, more detailed cloud features
uniform float noise_scale : hint_range(1.0, 12.0) = 5.5;
// What fraction of the surface is covered by cloud shadow (0=none, 1=all)
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.5;
// How dark the cloud shadows are (0=no effect, 1=pitch black)
uniform float cloud_shadow : hint_range(0.0, 1.0) = 0.45;

// ------------------------------------------------------------------
// Noise helpers
// ------------------------------------------------------------------

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

// Gradient (Perlin-style) noise, returns [-1, 1]
float gnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(dot(hash2(i),              f             ),
		    dot(hash2(i + vec2(1,0)), f - vec2(1,0)), u.x),
		mix(dot(hash2(i + vec2(0,1)), f - vec2(0,1)),
		    dot(hash2(i + vec2(1,1)), f - vec2(1,1)), u.x),
		u.y);
}

// Fractional Brownian Motion — 5 octaves with rotation to avoid grid artifacts
float fbm(vec2 p) {
	float v   = 0.0;
	float amp = 0.5;
	// Slight rotation between octaves breaks up axis-aligned banding
	mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
	for (int i = 0; i < 5; i++) {
		v   += amp * gnoise(p);
		p    = rot * p * 2.0;
		amp *= 0.5;
	}
	return v; // roughly in [-1, 1]
}

// ------------------------------------------------------------------
// Fragment
// ------------------------------------------------------------------

void fragment() {
	vec4 base = texture(TEXTURE, UV);

	// Leave fully-transparent pixels alone (outside the circle);
	// Godot fragment() doesn't allow early return, so branch the rest.
	if (base.a < 0.01) {
		COLOR = base;
	} else {

	// Polar coordinates centred on the texture
	vec2  centered = UV - vec2(0.5);
	float r        = length(centered);
	float angle    = atan(centered.y, centered.x);

	// Differential rotation:
	//   - at r→0 the rotation rate → speed * swirl_strength  (fast orbit)
	//   - at r→∞ the rotation rate → 0                       (slow / no orbit)
	// The formula speed * swirl / (r * swirl + 1) gives a smooth falloff.
	angle -= TIME * speed * swirl_strength / (r * swirl_strength + 1.0);

	// Back to Cartesian, scaled for noise
	vec2 q = vec2(cos(angle), sin(angle)) * r * noise_scale;

	// Slow lateral drift so the pattern doesn't just spin in circles
	q += vec2(TIME * speed * 0.07, TIME * speed * 0.04);

	// Two-layer domain warping (Inigo Quilez style):
	//   First FBM generates a displacement field...
	vec2 warp = vec2(
		fbm(q + vec2(1.7, 9.2)),
		fbm(q + vec2(8.3, 2.8))
	) * 0.65;
	//   ...which distorts the second FBM that gives us cloud shape.
	float cloud = fbm(q + warp);

	// Normalise to [0, 1]
	cloud = cloud * 0.5 + 0.5;

	// Soft threshold — controls coverage (lower threshold = more clouds)
	float threshold = 1.0 - cloud_coverage;
	float mask = smoothstep(threshold - 0.12, threshold + 0.12, cloud);

	// Fade only at the very centre point (avoids singularity), no edge fade
	mask *= smoothstep(0.02, 0.1, r);

	// Darken the base texture by the cloud mask
	float brightness = 1.0 - mask * cloud_shadow;
	COLOR = vec4(base.rgb * brightness, base.a);

	} // end else (base.a >= 0.01)
}
