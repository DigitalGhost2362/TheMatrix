---
name: matrix-vfx
description: "Build and iterate Unity ParticleSystem VFX through Matrix's vfx-* tools: plan layers, generate textures procedurally, write one compact JSON spec, read cheap text stats, look at one labelled time-lapse contact sheet, patch only what the critique names, score versions and keep the best. Holds the spec format, texture recipes, layering recipes per effect type, the symptom-to-cause table and mobile budgets. Use for any request to make, tune, fix or restyle a particle effect (hit, explosion, buff, pickup, trail, aura, fire, smoke, magic, UI sparkle)."
---

# Matrix VFX

Five tools, one prefab per effect, the prefab is the source of truth. Humans keep editing it in the
Inspector or in `Tools/Feeder/VFX Studio`; you read it back with `vfx-read` before patching.

| Tool | Use | Cost |
|---|---|---|
| `vfx-texture` | create/list procedural textures | text |
| `vfx-apply` | create or patch a prefab from a spec, returns version + stats | text (image only with `preview=sheet`) |
| `vfx-read` | current prefab as compact spec (non-defaults only) | text |
| `vfx-preview` | stats + one contact sheet (8 cells, labelled times) | ~1k tokens image, `statsOnly` = text |
| `vfx-history` | list / score / restore versions | text |

They ship disabled. Enable once: `tool-set-enabled-state` with
`vfx-apply, vfx-read, vfx-preview, vfx-texture, vfx-history`.

## The loop

1. **Brief** - restate the effect as a timeline: anticipation (0-0.1s), impact/peak, dissipation, total
   length; camera (3D world, top-down, side, UI); palette (2-3 colors + white-hot core); loop or one-shot.
2. **Plan layers in text** before any tool call: each emitter = role, texture, blend, timing, count.
   3-5 emitters is normal; more than 7 is rarely worth the overdraw.
3. **Textures** - `vfx-texture action=list` once; create only what is missing (one call each). Never ask
   the user for textures.
4. **Build** - one `vfx-apply` with the full spec and `replace=true`. Read the stats only.
5. **Fix numbers from stats** (no image): emitters that never emit, effect ending too early/late, peak over
   budget, fill above ~4x. Patch with small specs, still stats only.
6. **Look once** - `vfx-preview` (default 8 frames, 192px). Note the returned `frame=` and pass it to every
   later preview so scale stays comparable. Use `bg=#9A9A9A` once if the effect uses `alpha` blends.
7. **Critique against the brief per time cell**: silhouette/shape, timing, density, color/value contrast,
   readability. Name at most 3 problems, map each through the symptom table, patch only those fields.
8. **Score** each viewed version (`vfx-history action=score`, 0-1, note). Stop after 4 visual iterations
   or when score >= 0.85; `restore` the best version if the last one regressed.
9. Report: prefab path, final version, the 2-3 knobs a human is most likely to tweak (emitter.module.field).

Effects that decorate a model (weapon aura, pickup glow): emit from the mesh surface with
`shape: {shapeType: Mesh, mesh: "<fbx path>", meshShapeType: Triangle, position/rotation/scale}` copied from
the model's mesh child transform, keep emitters at identity, `simulationSpace: Local` so it follows the model.
Preview with `context=<model prefab>`; for the final look place an instance in a showcase scene and use
`camera=<camera name>` so bloom/tonemapping match the game. Compare side by side with the reference frames
at the same orientation, not from memory.

Token rules: never call `gameobject-component-get`/`particle-system-get` on effects (huge); never re-send
unchanged emitters; prefer stats over images; one sheet per iteration, not per patch.

## Spec

```json
{
  "defaults": { "main": { "duration": 0.5 } },
  "root": { "main": { "loop": false } },
  "emitters": [
    { "name": "Core", "material": "vfx_glow/add", "pos": [0,0,0],
      "main": { "startLifetime": [0.15,0.25], "startSpeed": 0, "startSize": [1.2,1.6], "startColor": "#FFD27A" },
      "emission": { "rateOverTime": 0, "bursts": [[0,1]] },
      "shape": false,
      "sizeOverLifetime": { "size": "0:0.3,0.15:1,1:0" },
      "colorOverLifetime": { "color": "#FFFFFF@0,#FF8A00@0.6|1@0,0@1" } },
    { "name": "OldLayer", "remove": true }
  ]
}
```

- Emitters are matched by `name`; unknown names are created; `remove:true` deletes; `replace=true` rebuilds all.
- Emitter keys: `name, parent, pos, rot, scale, material, trailMaterial, remove`, any module, `renderer`.
- Module keys and property names are exactly the Unity scripting API names (`ParticleSystem.MainModule`
  etc.): `main, emission, shape, velocityOverLifetime, limitVelocityOverLifetime, inheritVelocity,
  lifetimeByEmitterSpeed, forceOverLifetime, colorOverLifetime, colorBySpeed, sizeOverLifetime, sizeBySpeed,
  rotationOverLifetime, rotationBySpeed, externalForces, noise, collision, trigger, subEmitters,
  textureSheetAnimation, lights, trails, customData`. `{...}` enables the module, `false` disables it.
  Unknown keys come back as errors listing the valid names - fix and resend only that part.
- `...Multiplier` properties are folded into their curve; rotations (`startRotation*`, `rotationOverLifetime`,
  `rotationBySpeed`) are **degrees**.
- `renderer`: `renderMode` (Billboard|Stretch|HorizontalBillboard|VerticalBillboard|Mesh|None), `sortMode`,
  `sortingFudge`, `sortingOrder`, `minParticleSize`, `maxParticleSize`, `alignment`, `pivot`, `flip`,
  `lengthScale`, `velocityScale`, `cameraVelocityScale`, `normalDirection`, `allowRoll`, `mesh` (asset path),
  `maskInteraction`, `shadowCastingMode`, `receiveShadows`, `enableGPUInstancing`.
- `material`: `"<texture>/<blend>"` with blend `add|alpha|premul|mul` (one shared material per pair is
  created in `Assets/VFX/_Materials`), or a `.mat` path, or `"none"`. New emitters default to
  `vfx_glow/add`. A flipbook texture auto-enables `textureSheetAnimation` with its tiles.
- `emission.bursts`: `[[time,count]]` or `[[time,count,cycles,interval,probability]]`; count may be `[min,max]`.
- `subEmitters`: `{"list":[{"emitter":"Sparks","type":"Death","inherit":"InheritColor|InheritSize","p":1}]}`.
  The target is moved under its owner automatically; give it `emission: {rateOverTime:0}` so it only fires
  from the parent.
- `parent`: another emitter's name (nest for organisation or local-space following).
- `defaults` is merged into every newly created emitter only; `root` is the controller ParticleSystem on the
  prefab root (no emission/renderer) - Play/Stop on it drives the whole effect. `root.scale` (a number or
  `[x,y,z]`) scales the prefab root, which resizes the whole effect; `vfx-read` lists it only when it is not 1.

Encodings:

| Kind | Forms |
|---|---|
| number curve (MinMaxCurve) | `2` · `[1,3]` random · `"0:0,0.2:1,1:0"` curve · `{"c":"curve","m":5}` · `{"min":"curve","max":"curve","m":2}` |
| curve keys | `t:v` auto-smooth, `t:v:inTangent:outTangent` explicit |
| color | `#RRGGBB`, `#RRGGBBAA`, HDR `#RRGGBB*3` - keep every channel that must stay below white under 1 after the multiplier (`#FF7A00*1.6` stays orange, `#FFA000*1.8` clips to lemon yellow) |
| gradient | `#FFFFFF@0,#FF8000@0.5\|1@0,0@1` (color keys, `\|`, alpha keys), suffix `~fixed` |
| color/gradient (MinMaxGradient) | `#hex` · `["#a","#b"]` · `"gradient"` · `["grad","grad"]` · `{"rand":"gradient"}` (3+ discrete colors: `{"rand":"#a@0.4,#b@0.7,#c@1~fixed"}`) |
| vector | `[x,y,z]` |
| enum / flags | `"Sphere"`, `"InheritColor\|InheritSize"` |

New-emitter baseline (what `vfx-read` omits): one-shot, duration 1, lifetime 1, speed 2, size 0.5,
rate 10/s, sphere r=0.1, scalingMode Hierarchy, `vfx_glow/add`.

## Textures (`vfx-texture`)

White RGB, shape in alpha, 128px default (use 256 for big hero sprites, 512 for 4x4 flipbooks).

| shape | look | key params |
|---|---|---|
| glow | soft dot | falloff (1 soft .. 4 tight) |
| circle | disc | soft, hollow (0-1 outline) |
| ring | thin ring | radius, width, soft |
| shockwave | ring + faint inner fill | radius, width |
| spark | horizontal streak, for Stretch billboards | thin, falloff, angle |
| star | n-point star | points, inner, sharp, glow |
| sparkle | pointed 4-point twinkle (astroid) | sharp (0.4 needle .. 1 diamond), falloff, glow |
| crescent | thin moon arc | offset (0.1 thin .. 0.5 thick), radius, soft |
| flare | cross / lens flare | rays, thin, glow |
| smoke | noisy puff; `frames:[4,4]` = evolving flipbook | scale, octaves, contrast, evolve, seed |
| noise | tiling fbm (dissolve, distortion) | scale (int period), contrast, seed |
| trail | soft band for trails / ribbons | soft |
| polygon | triangle..hexagon, shards | sides, soft, hollow, angle |
| lightning | jagged bolt, `frames` for flicker | thin, seed, evolve |

All shapes accept `glow` (soft halo 0-1), `angle`, `size`, `seed`.
Suggested starter set: `vfx_glow` (auto), `spark {shape:spark,thin:0.12}`, `ring {shape:ring}`,
`smoke4 {shape:smoke,size:512,frames:[4,4]}`, `star4 {shape:star,points:4,glow:0.4}`.

## Layering recipes

| Effect | Layers (emitter: texture/blend, timing) |
|---|---|
| Hit / impact (0.4s) | Flash: glow/add 1 burst, life .08-.12, big, white->color; Sparks: spark/add, Stretch, burst 12-20, speed 6-12, gravity .5, life .2-.4, size shrink; Ring: ring/add, burst 1, size 0->2 ease-out, alpha fade; optional Dust: smoke4/alpha 3-5 puffs |
| Explosion (1.5s) | Flash; Fireball: smoke4/add orange->red, burst 8-12, speed 1-3, size grow; Smoke: smoke4/alpha dark, starts .1s, rises, life 1-1.5; Sparks/debris with gravity; Shockwave ring flat (`renderer.renderMode: HorizontalBillboard`) |
| Pickup / collect | Burst of star4/add sparkles spiralling (`velocityOverLifetime.orbitalY`), short glow pop, small ring |
| Buff / aura (loop) | Ground ring HorizontalBillboard slow rotate; rising motes glow/add `rateOverTime` 8-15, noise; soft column glow alpha .2 |
| Heal | green-white motes rising with noise, plus/cross flare, soft ground ring |
| Fire (loop) | flames smoke4/add yellow->red over life, cone up, speed 1-2, size shrink; embers spark/add sparse with noise; smoke smoke4/alpha above |
| Magic projectile | core glow/add (Local space); trail via `trails` module + `trailMaterial: "trail/add"`; sparkle `emission.rateOverDistance` in World space |
| Lightning | lightning/add flipbook, few particles, very short life .05-.1 with bursts repeated by `cycles` |
| UI sparkle | star4/add, tiny count, `main.startSize` in screen-sized units, view=front |

Timing rules: impact frames are front-loaded (peak by 0.05-0.1s); ease-out sizes (`"0:0,0.2:1,1:0.8"`);
alpha ends at 0; hot core is near-white, color lives in the mid/outer layers; vary lifetime/size with `[min,max]`.

## Symptom -> cause -> fix

| Seen in sheet / stats | Likely cause | Fix |
|---|---|---|
| emitter never emits | rate 0 and no bursts; burst time > duration; sub-emitter with no parent deaths | add bursts/rate; burst time < duration; check subEmitters list |
| everything appears at once then vanishes | lifetime too short, no size/alpha curves | lengthen lifetime, add sizeOverLifetime + alpha fade |
| hard square edges | wrong texture/blend, material none | use a glow/smoke texture, `material "x/add"` |
| colors look lemon-yellow / white instead of orange-gold | HDR multiplier pushed green over 1 | lower the multiplier or the green channel (`#FF7A00*1.6`) |
| glow looks flat, no halo | preview has no post-processing | judge glow with `vfx-preview camera=...` in a scene with bloom |
| washed-out white blob | too many additive layers overlapping, bright colors | lower count/size, darken color, move a layer to `alpha` |
| invisible on light background | additive on bright bg | add a dark `alpha` layer under it or use `alpha` blend |
| sparks look like dots | Billboard render mode | `renderer.renderMode: Stretch`, `velocityScale` .05-.15, spark texture |
| sparks sideways | texture orientation | regenerate spark with `angle: 90` |
| motion too uniform / CG | constant speed and size | `[min,max]` ranges, `noise` strength .2-.5, gravity |
| effect too small / large in frame | size or speed scale | scale `startSize`/`startSpeed` together, or `root.scale` |
| ends too late / lingering | long lifetimes, looping | shorten lifetimes, `main.loop:false` |
| peak over budget / fill > 4x | too many or too large particles | reduce counts first, then size; merge layers |
| flipbook plays too fast / not at all | tiles or frameOverTime | `textureSheetAnimation` numTilesX/Y, `cycleCount` |

## Budgets (mobile)

Peak alive <= 150 per effect (hard 300), fill <= 3x, <= 5 emitters, textures 128-256 (512 only for 4x4
flipbooks), one material per texture/blend pair (shared, batches), no realtime `lights` module, no
`collision` with World mode unless needed.
