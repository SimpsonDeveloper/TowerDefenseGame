using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core.ops;

/// <summary>
/// Scramble's numbers. Authored alongside the behaviour in
/// <c>effect-vocab/ops/primitives/scramble.md</c> — that file is the source, this record is the
/// port.
/// </summary>
/// <param name="SecondsPerEnergy">Seconds of shutdown per unit of edge energy.</param>
/// <param name="MaxSeconds">Ceiling on one application, so a huge lattice cannot switch a shield
///   off for the whole wave.</param>
public sealed record ScrambleTuning(
    double SecondsPerEnergy = 1.0 / 40.0,
    double MaxSeconds = 6.0);

/// <summary>
/// <b>Scramble</b> (Citrine · Citrine) — an EMP that switches a shield off, per
/// <c>effect-vocab/ops/primitives/scramble.md</c>.
///
/// It deals no damage. It disables the shield <b>without spending it</b>, which is the whole
/// point: breaking a shield by damage means paying its worth in HP first, and this skips that bill
/// entirely. While it is down, damage lands on HP unblocked and Short-circuit has something to
/// consume.
///
/// <b>A bet on the wave, not a tax.</b> Against a shield-heavy enemy it skips a whole bar; against
/// an HP-only one there is nothing to disable and the crystal is wasted
/// (<c>effect-vocab/vocab-overview/shield.md</c>). Nothing here checks for that — the shield
/// component ignores a shutdown it has no shield for, so the waste is real rather than refunded.
///
/// No <see cref="ITickingState"/> face: the shutdown's clock belongs to the shield itself, and the
/// <c>Shield-down</c> state is mirrored back from it rather than being counted down here.
/// </summary>
public sealed class Scramble : IOp
{
    private readonly ScrambleTuning _tuning;

    public Scramble(ScrambleTuning tuning = null) => _tuning = tuning ?? new ScrambleTuning();

    public OpId Id => OpId.Scramble;

    public void Apply(ShotContext context, double quantity, EnemyState target)
    {
        if (quantity <= 0) return;

        double seconds = quantity * _tuning.SecondsPerEnergy;
        target.DealShieldDisable(seconds < _tuning.MaxSeconds ? seconds : _tuning.MaxSeconds);
    }
}
