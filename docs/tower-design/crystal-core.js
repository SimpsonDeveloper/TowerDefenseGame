// crystal-core.js — shared operator algebra for the crystal dataflow system.
// Loaded as a classic script by dataflow-playground.html and operator-reference.html,
// so tuning lives in exactly one place.
//
// MODEL (conservation routing): the base shot (power = E_flow, plus base rate/speed/range) is
// divided among the sources by weight, then routed. Routing CONSERVES every stat — a ▲ divides
// each stat among its outputs, a ▽ sums each stat. Each crystal applies ONE effect ON TOP, and
// the effect depends on ORIENTATION: a ▲ uses its `split` facet (the lighter, spread version),
// a ▽ uses its `merge` facet (the heavier "focus"/combine version, acting on the summed stream).
// Damage = power × rate; the weapon = the sum of all sinks.

const BASE_DEFAULT = {power:10, type:'kinetic', speed:5, range:5, pierce:0, slow:0, rate:1.0};

// Each crystal has a split (▲) and a merge (▽) facet: a pure single-shot transform fn(shot,params),
// a tunable param set, and a human formula. Multiplicative on nonzero-base stats (power/rate/
// speed/range); additive on stats that start at zero (slow/pierce). Numbers are tunable placeholders.
const OPS = {
  ruby:{ name:'Ruby', color:'#e6394a', cost:28,
    split:{ label:'Amplify', params:{power:1.7},
      fn:(s,p)=>({...s, power:s.power*p.power}),
      formula:p=>`power × ${p.power}` },
    merge:{ label:'Concentrate', params:{power:2.4},
      fn:(s,p)=>({...s, power:s.power*p.power}),
      formula:p=>`power × ${p.power}` } },
  emerald:{ name:'Emerald', color:'#2ecc71', cost:22,
    split:{ label:'Accelerate', params:{rate:1.7},
      fn:(s,p)=>({...s, rate:s.rate*p.rate}),
      formula:p=>`rate × ${p.rate}` },
    merge:{ label:'Volley', params:{rate:1.4, power:1.4},
      fn:(s,p)=>({...s, rate:s.rate*p.rate, power:s.power*p.power}),
      formula:p=>`rate × ${p.rate}, power × ${p.power}` } },
  sapphire:{ name:'Sapphire', color:'#3aa0ff', cost:16,
    split:{ label:'Chill', params:{slow:20},
      fn:(s,p)=>({...s, slow:s.slow+p.slow, type:'ice'}),
      formula:p=>`slow + ${p.slow}%, type → ice` },
    merge:{ label:'Deep Freeze', params:{slow:50},
      fn:(s,p)=>({...s, slow:s.slow+p.slow, type:'ice'}),
      formula:p=>`slow + ${p.slow}%, type → ice` } },
  citrine:{ name:'Citrine', color:'#f1c40f', cost:12,
    split:{ label:'Extend', params:{range:1.6},
      fn:(s,p)=>({...s, range:s.range*p.range}),
      formula:p=>`range × ${p.range}` },
    merge:{ label:'Railgun', params:{speed:1.8, range:1.2},
      fn:(s,p)=>({...s, speed:s.speed*p.speed, range:s.range*p.range}),
      formula:p=>`speed × ${p.speed}, range × ${p.range}` } },
  amethyst:{ name:'Amethyst', color:'#a974ff', cost:20,
    split:{ label:'Pierce', params:{pierce:2},
      fn:(s,p)=>({...s, pierce:s.pierce+p.pierce}),
      formula:p=>`pierce + ${p.pierce}` },
    merge:{ label:'Lance', params:{pierce:4, power:1.2},
      fn:(s,p)=>({...s, pierce:s.pierce+p.pierce, power:s.power*p.power}),
      formula:p=>`pierce + ${p.pierce}, power × ${p.power}` } },
  quartz:{ name:'Quartz', color:'#d7e1f4', cost:6,   // pure routing tool — identity in both forms
    split:{ label:'Wire', params:{}, fn:(s)=>({...s}), formula:()=>`pass through unchanged` },
    merge:{ label:'Wire', params:{}, fn:(s)=>({...s}), formula:()=>`pass through unchanged` } },
};

// ---- tunable params, persisted to localStorage so both pages share them ----
const PARAM_KEY = 'crystalParams';
function loadOverrides(){ try{ return JSON.parse(localStorage.getItem(PARAM_KEY) || '{}'); }catch(e){ return {}; } }
function saveOverrides(o){ try{ localStorage.setItem(PARAM_KEY, JSON.stringify(o)); }catch(e){} }
let OVERRIDES = loadOverrides();

function params(key, slot){                       // effective params for one facet (slot = 'split'|'merge')
  slot = slot || 'split';
  const def = OPS[key][slot].params;
  const ov = (OVERRIDES.ops && OVERRIDES.ops[key] && OVERRIDES.ops[key][slot]) || {};
  return {...def, ...ov};
}
function BASE(){ return {...BASE_DEFAULT, ...(OVERRIDES.base || {})}; }
function applyFacet(key, s, slot){ return OPS[key][slot].fn(s, params(key, slot)); }  // slot picked by orientation

function fmtShot(s){
  const p = [Math.round(s.power)+' dmg'];
  if(s.type !== 'kinetic') p.push(s.type);
  if(s.slow > 0) p.push('slow '+Math.round(s.slow)+'%');
  if(s.pierce > 0) p.push('pierce '+Math.round(s.pierce));
  p.push((s.rate||0).toFixed(1)+'/s');
  return p.join(' · ');
}

// palette metadata consumed by the playground
const CRYSTALS = {};
for(const k in OPS){ CRYSTALS[k] = { name:OPS[k].name, color:OPS[k].color, cost:OPS[k].cost,
  splitDesc:OPS[k].split.label, mergeDesc:OPS[k].merge.label }; }
