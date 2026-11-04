using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core.ops;

/// <summary>
/// Purify's numbers. Authored alongside the behaviour in
/// <c>effect-vocab/ops/primitives/purify.md</c> — that file is the source, this record is the port.
/// </summary>
/// <param name="SecondsPerEnergy">Seconds of buff shutdown per unit of edge energy.</param>
/// <param name="MaxSeconds">Ceiling on one application. Generous, because the window has to last
///   long enough to actually kill the pack it was aimed at — a shutdown that expires first
///   accomplished nothing.</param>
public sealed record PurifyTuning(
    double SecondsPerEnergy = 1.0 / 24.0,
    double MaxSeconds = 12.0);

/// <summary>
/// <b>Purify</b> (Quartz · Quartz) — switches an enemy's buffs off, per
/// <c>effect-vocab/ops/primitives/purify.md</c>. The last of the seven primitives.
///
/// <b>It suppresses rather than strips</b>, and that is the whole design. The one buff in the game
/// is huddle regen (<c>effect-vocab/vocab-overview/buffs.md</c>), which is <i>derived</i> from how
/// many of its own kind an enemy is standing near — so there is no stored buff to delete. A
/// <see cref="EnemyState.Clear"/> here would be undone by the next recount, making for a visibly
/// useless op. Instead this queues a timed shutdown that holds the regen off while the pack is
/// still tight: the same shape as Scramble against a shield, switch it off without having to spend
/// it.
///
/// <b>Never wasted, unlike Scramble.</b> A shield is a property of the unit, so scrambling an
/// unshielded enemy does nothing and that is the bet. Any enemy can end up in a crowd, so a
/// shutdown landing on a lone one is a pre-emptive cleanse rather than a miss — it still holds
/// when the pack re-forms inside the window.
///
/// Quartz is the cheapest crystal, so an Qz–Qz edge keeps nearly all the energy that reaches it.
/// That is the catalyst earning its keep: pure utility, no damage, and a long window for the toll
/// of a rounding error.
/// </summary>
public sealed class Purify : IOp
{
    private readonly PurifyTuning _tuning;

    public Purify(PurifyTuning tuning = null) => _tuning = tuning ?? new PurifyTuning();

    public OpId Id => OpId.Purify;

    public void Apply(ShotContext context, double quantity, EnemyState target)
    {
        if (quantity <= 0) return;

        double seconds = quantity * _tuning.SecondsPerEnergy;
        target.DealBuffSuppress(seconds < _tuning.MaxSeconds ? seconds : _tuning.MaxSeconds);
    }
}
