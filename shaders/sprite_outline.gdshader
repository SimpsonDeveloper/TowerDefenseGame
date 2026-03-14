shader_type canvas_item;

/// Width of the outline/glow in pixels.
uniform float outline_width : hint_range(1.0, 8.0) = 3.0;

/// Color of the outline. Alpha is multiplied by the computed glow strength.
uniform vec4 outline_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

/// Multiplier on the glow falloff — higher values make the glow brighter/wider.
uniform float glow_intensity : hint_range(0.0, 3.0) = 1.5;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    vec2 texel = TEXTURE_PIXEL_SIZE;

    // For each transparent pixel, find the maximum weighted alpha contribution
    // from neighboring opaque pixels. Weight falls off linearly with distance
    // so pixels close to the edge glow brightest.
    float max_neighbor_alpha = 0.0;
    int steps = int(ceil(outline_width));

    for (int x = -steps; x <= steps; x++) {
        for (int y = -steps; y <= steps; y++) {
            float dist = length(vec2(float(x), float(y)));
            if (dist == 0.0 || dist > outline_width) continue;

            float neighbor_alpha = texture(TEXTURE, UV + vec2(float(x), float(y)) * texel).a;
            float falloff = 1.0 - (dist / outline_width);
            max_neighbor_alpha = max(max_neighbor_alpha, neighbor_alpha * falloff);
        }
    }

    if (tex.a < 0.1) {
        // Transparent pixel: render glow contribution from nearby opaque pixels.
        float glow_alpha = clamp(max_neighbor_alpha * glow_intensity, 0.0, 1.0);
        COLOR = vec4(outline_color.rgb, glow_alpha * outline_color.a);
    } else {
        // Opaque pixel: render the sprite normally.
        COLOR = tex;
    }
}
