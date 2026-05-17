shader_type canvas_item;

// 0 = beam invisible. 1 = beam at full visible width and whited out.
// Can be pushed above 1 for an even more blown-out look.
uniform float intensity : hint_range(0.0, 1.5) = 0.874;

// Softness of the alpha falloff at the beam's outer edge.
uniform float edge_softness : hint_range(0.001, 1.0) = 0.477;

// Softness of the core-to-base transition.
uniform float core_softness : hint_range(0.001, 1.0) = 1.0;

// Intensity at which the white-hot core first becomes visible.
uniform float core_threshold : hint_range(0.0, 1.0) = 0.16;

// How sharply the core ramps in past the threshold. Higher = colder ramp-up,
// more "thermodynamic" feel (long dull glow before any white appears).
uniform float core_curve : hint_range(1.0, 8.0) = 1.0;

void fragment() {
    vec4 base = texture(TEXTURE, UV);

    // 0 at the horizontal equator, 1 at the top/bottom of the sprite.
    float d = abs(UV.y - 0.5) * 2.0;

    // The whole beam's visible half-width grows with intensity.
    float beam_size = clamp(intensity, 0.0, 1.0);

    // How "lit" the core is. Stays at 0 until intensity passes the threshold,
    // then ramps in along a curve so the white emerges gradually like real heat.
    float heat = pow(clamp((intensity - core_threshold) / max(1.0 - core_threshold, 0.001), 0.0, 1.0), core_curve);

    // Core occupies a growing fraction of the beam, but only once it's lit.
    // Past full heat the core ratio exceeds the beam, flooding it with white.
    float core_ratio = heat * 1.4;

    // Normalize distance inside the beam: 0 at equator, 1 at the visible edge.
    float dn = (beam_size > 0.0001) ? d / beam_size : 1.0;

    // White-hot mask: shape from distance, brightness gated by heat.
    float core = 1.0 - smoothstep(core_ratio - core_softness, core_ratio, dn);
    core = clamp(core, 0.0, 1.0) * heat;

    vec3 col = mix(base.rgb, vec3(1.0), core);

    // Alpha: full inside the beam, faded near the visible edge, and the
    // whole thing scales with intensity so the beam fades out completely at 0.
    float inside = 1.0 - smoothstep(1.0 - edge_softness, 1.0, dn);
    float alpha = base.a * inside * smoothstep(0.0, 0.15, intensity);

    COLOR = vec4(col, alpha);
}