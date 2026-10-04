using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core.ops;

/// <summary>
/// Mind-damage's numbers. Authored alongside the behaviour in
/// <c>effect-vocab/ops/primitives/mind-damage.md</c> — that file is the source, this record is the
/// port.
/// </summary>
/// <param name="MindPerEnergy">Mind drained per unit of edge energy. The op's whole tuning surface:
///   there is no state, no curve and no duration to shape.</param>
public sealed record MindDamageTuning(double MindPerEnergy = 1.0 / 20.0);

/// <summary>
/// <b>Mind-damage</b> (Amethyst · Amethyst) — damage routed to the second bar, per
/// <c>effect-vocab/ops/primitives/mind-damage.md</c>.
///
/// It takes a single crystal to carry mind and <b>two in sequence</b> to convert a stream to it,
/// which is the whole reason this is an Am–Am combo and not an Amethyst property. To hit HP and Mind
/// both, split the stream and route one branch through a pair — the energy divides, so reach into
/// one bar is paid for out of the other.
///
/// <b>Not a state.</b> Mind is an innate per-enemy meter, only ever drained: nothing stacks, nothing
/// merges, nothing expires, and no tick restores it. The drain is permanent for the enemy's life.
/// That makes this the simplest op in the vocabulary — one line of arithmetic and no second face.
///
/// <b>Magnitude is the op's quantity</b>, like every other op: the energy that crossed the
/// producing edge. The design doc's older phrasing had it come from a typed stream's "power"
/// stat, which the compiler never grew — it routes energy and names ops, nothing else.
/// </summary>
public sealed class MindDamage : IOp
{
    private readonly MindDamageTuning _tuning;

    public MindDamage(MindDamageTuning tuning = null) => _tuning = tuning ?? new MindDamageTuning();

    public OpId Id => OpId.MindDamage;

    public void Apply(ShotContext context, double quantity, EnemyState target)
    {
        if (quantity <= 0) return;

        target.DealMindDamage(quantity * _tuning.MindPerEnergy);
    }
}
