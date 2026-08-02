// crystal-core.js — shared crystal + combo data for the dataflow system.
// Loaded as a classic script by dataflow-playground.html and operator-reference.html,
// so the roster and the combo matrix live in exactly one place.
//
// MODEL: there are no per-crystal stat "facets" anymore. The only quantity that flows is
// ENERGY. Each source is seeded the full core energy; each crystal draws its own `cost` from
// the stream as it passes (local toll); a ▲ divides the remainder among its outputs, a ▽ sums
// its inputs (see energy-conservation.md). A crystal does nothing on its own — EFFECTS come
// from COMBOS: two crystals adjacent in the flow produce the op named in the matrix below,
// scaled by the energy arriving at the downstream crystal. This file only NAMES the ops
// (stubs); their behavior is authored one-per-file under effect-vocab/ops/ and implemented
// later in the Godot / C# port.

// ---- crystal roster (palette metadata) ----
// element is a flavor note (the inspiration for each crystal's ops); it carries no mechanics.
// cost = energy the crystal draws as the stream passes it.
const CRYSTALS = {
  ruby:     { name:'Ruby',     color:'#e6394a', cost:28, element:'Fire' },
  sapphire: { name:'Sapphire', color:'#3aa0ff', cost:16, element:'Ice' },
  emerald:  { name:'Emerald',  color:'#2ecc71', cost:22, element:'Nature / Acid' },
  citrine:  { name:'Citrine',  color:'#f1c40f', cost:12, element:'Lightning' },
  amethyst: { name:'Amethyst', color:'#a974ff', cost:20, element:'Arcane / Mind' },
  quartz:   { name:'Quartz',   color:'#d7e1f4', cost:6,  element:'Pure' },
};

// stable column/row order for rendering the matrix
const CRYSTAL_ORDER = ['ruby','sapphire','emerald','citrine','amethyst','quartz'];

// ---- combo matrix (SOURCE MIRROR of effect-vocab/vocab-overview/combo-matrix.md) ----
// Symmetric: COMBO[a][b] === COMBO[b][a]. Diagonal = a crystal's single-crystal native op
// (produced when two of that crystal are adjacent). Off-diagonal = the two-crystal op.
const COMBO = {
  ruby:     { ruby:'Burn',      sapphire:'Frostburn',      emerald:'Accelerant', citrine:'Fire Arc',      amethyst:'Mark',        quartz:'Flareup' },
  sapphire: { ruby:'Frostburn', sapphire:'Chill → Freeze', emerald:'Weather',    citrine:'Frost Arc',     amethyst:'Numb',        quartz:'Shatter' },
  emerald:  { ruby:'Accelerant',sapphire:'Weather',        emerald:'Corrode',    citrine:'Acid Arc',      amethyst:'Hex',         quartz:'Dissolve' },
  citrine:  { ruby:'Fire Arc',  sapphire:'Frost Arc',      emerald:'Acid Arc',   citrine:'Scramble',      amethyst:'Detonate',    quartz:'Short-circuit' },
  amethyst: { ruby:'Mark',      sapphire:'Numb',           emerald:'Hex',        citrine:'Detonate',      amethyst:'Mind-damage', quartz:'Focus' },
  quartz:   { ruby:'Flareup',   sapphire:'Shatter',        emerald:'Dissolve',   citrine:'Short-circuit', amethyst:'Focus',       quartz:'Purify' },
};

// op name -> its stub doc, relative to docs/tower-design/ (behavior lives there, not here)
const OP_FILES = {
  'Burn':'../effect-vocab/ops/primitives/burn.md',
  'Chill → Freeze':'../effect-vocab/ops/primitives/chill-freeze.md',
  'Corrode':'../effect-vocab/ops/primitives/corrode.md',
  'Mark':'../effect-vocab/ops/primitives/mark.md',
  'Scramble':'../effect-vocab/ops/primitives/scramble.md',
  'Mind-damage':'../effect-vocab/ops/primitives/mind-damage.md',
  'Purify':'../effect-vocab/ops/primitives/purify.md',
  'Frostburn':'../effect-vocab/ops/interactives/frostburn.md',
  'Shatter':'../effect-vocab/ops/interactives/shatter.md',
  'Flareup':'../effect-vocab/ops/interactives/flareup.md',
  'Dissolve':'../effect-vocab/ops/interactives/dissolve.md',
  'Detonate':'../effect-vocab/ops/interactives/detonate.md',
  'Focus':'../effect-vocab/ops/interactives/focus.md',
  'Hex':'../effect-vocab/ops/interactives/hex.md',
  'Fire Arc':'../effect-vocab/ops/interactives/fire-arc.md',
  'Frost Arc':'../effect-vocab/ops/interactives/frost-arc.md',
  'Acid Arc':'../effect-vocab/ops/interactives/acid-arc.md',
  'Numb':'../effect-vocab/ops/interactives/numb.md',
  'Accelerant':'../effect-vocab/ops/interactives/accelerant.md',
  'Weather':'../effect-vocab/ops/interactives/weather.md',
  'Short-circuit':'../effect-vocab/ops/interactives/short-circuit.md',
};

// the op a pair of adjacent crystals produces (order-independent)
function comboOp(a, b){ return (COMBO[a] && COMBO[a][b]) || '—'; }

// native (single-crystal) op for a crystal, i.e. its diagonal cell
function nativeOp(key){ return comboOp(key, key); }
