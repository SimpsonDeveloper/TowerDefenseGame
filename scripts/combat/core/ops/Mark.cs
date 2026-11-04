using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core.ops;

/// <summary>
/// Mark's numbers. Authored alongside the behaviour in
/// <c>effect-vocab/ops/primitives/mark.md</c> — that file is the source, this record is the port.
/// </summary>
/// <param name="SecondsPerEnergy">Seconds the mark holds per unit of edge energy. Duration is the
///   op's <b>only</b> dimension: the flag carries no magnitude, so energy can buy nothing but
///   time.</param>
/// <param name="MaxSeconds">Ceiling on one application. A mark is a window for a second tower to
///   shoot into, and an uncapped one would just be a permanent property of the enemy.</param>
public sealed record MarkTuning(
    double SecondsPerEnergy = 1.0 / 30.0,
    double MaxSeconds = 8.0);

/// <summary>
/// <b>Mark</b> (Amethyst · Ruby) — a timed tag, per
/// <c>effect-vocab/ops/primitives/mark.md</c>.
///
/// It deals no damage, slows nothing, and ticks nothing. <b>Mark does not do anything</b> — it is
/// a flag other ops read, and the whole op is one <see cref="EnemyState.SetFlag"/> call. Its value
/// is entirely in its consumers, Focus and Detonate, neither of which exists yet: until one does,
/// a marked enemy is a line in the debug readout and nothing more. That is the expected state, not
/// a gap — "missing handler = no-op" cuts both ways, and a state with no reader is as inert as an
/// op with no implementation (<see cref="CombatRules"/>).
///
/// <b>A re-mark can only extend.</b> Merge for a timed flat is max, not sum and not replace
/// (<c>merge.md</c>), and <see cref="EnemyState.SetFlag"/> already does exactly that — so a
/// cheap second mark cannot cut a long one short, and two towers marking together cannot stack a
/// window neither of them paid for. Nothing op-specific is needed to get that; it falls out of
/// writing the state the ordinary way.
///
/// No <see cref="ITickingState"/> face and no clock of its own: a timed flag is counted down by
/// <see cref="EnemyState.Tick"/> and clears itself when it runs out.
/// </summary>
public sealed class Mark : IOp
{
    private readonly MarkTuning _tuning;

    public Mark(MarkTuning tuning = null) => _tuning = tuning ?? new MarkTuning();

    public OpId Id => OpId.Mark;

    public void Apply(ShotContext context, double quantity, EnemyState target)
    {
        if (quantity <= 0) return;

        double seconds = quantity * _tuning.SecondsPerEnergy;
        target.SetFlag(StateId.Mark, seconds < _tuning.MaxSeconds ? seconds : _tuning.MaxSeconds);
    }
}
