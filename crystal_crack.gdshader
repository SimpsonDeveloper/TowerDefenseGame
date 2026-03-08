shader_type canvas_item;

uniform float crack_progress : hint_range(0.0, 1.0) = 0.0;

// Deterministic hash → two pseudo-random values per cell
vec2 hash2(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(p) * 43758.5453123);
}

// Worley noise — returns proximity to the nearest cell edge.
// Low values = near a crack line; high values = inside a cell.
float worley(vec2 uv, float scale) {
    vec2 scaled = uv * scale;
    vec2 cell  = floor(scaled);
    vec2 local = fract(scaled);

    float d1 = 8.0, d2 = 8.0;
    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            vec2 neighbor = cell + vec2(float(dx), float(dy));
            vec2 point    = hash2(neighbor);
            vec2 diff     = vec2(float(dx), float(dy)) + point - local;
            float d = dot(diff, diff);
            if (d < d1) { d2 = d1; d1 = d; }
            else if (d < d2) { d2 = d; }
        }
    }
    // Edge distance: difference between the two nearest cell distances
    return sqrt(d2) - sqrt(d1);
}

void fragment() {
    vec4 col = texture(TEXTURE, UV);

    // Snap UV to the center of the nearest texel so the crack pattern steps
    // in discrete pixel-aligned increments, matching the sprite's pixel grid.
    vec2 pixelUV = (floor(UV / TEXTURE_PIXEL_SIZE) + 0.5) * TEXTURE_PIXEL_SIZE;

    // Two scales of cracks: coarse structure + finer detail
    float edge = worley(pixelUV, 3.5) * 0.6 + worley(pixelUV, 7.0) * 0.4;

    // Threshold creeps upward with progress, revealing more crack lines.
    // Multiplied by active flags so cracks are fully suppressed when progress
    // is zero or the pixel is transparent — no branching needed.
    float active = step(0.01, crack_progress) * step(0.01, col.a);
    float crack  = (1.0 - smoothstep(0.0, crack_progress * 0.35, edge)) * active;

    // Dark blue-grey crack color blended over the original
    vec3 crackColor = vec3(0.04, 0.04, 0.10);
    COLOR = vec4(mix(col.rgb, crackColor, crack * 0.88), col.a);
}
