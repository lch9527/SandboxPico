# Classic reference and adaptation

Reference: Shacknews, “Asif beats Xortex for the first time”, April 9, 2016.
Article: https://www.shacknews.com/article/94042/htc-vive-the-lab-gameplay---asif-beats-xortex-for-the-first-time
Video: https://www.youtube.com/watch?v=ktnPHgFwV5E

The video was opened and inspected in the browser on November 2, 2026. The observations below are sampled frames, not a frame-by-frame reconstruction of Valve's encounter scripts. Exact spawn times, world-space distances, hidden enemies, damage values, progression triggers, and boss thresholds cannot be established from these samples. **This delivery is an authored Classic-inspired adaptation, not a verified 1:1 recreation.**

| Video time | Directly visible observation | HandFleet use / uncertainty |
|---|---|---|
| 00:48 | Player is beside the arcade cabinet in The Lab hub. | Hub excluded; retain existing MetaHackathon menu. |
| 01:36 | Circular enclosed arena, cyan player effects, separate red bead streams, score display. | Approach introduces aimed bead bursts. Exact formation and entry timing inferred. |
| 02:24 | Multiple surrounding targets, straight red crossing lines and bead streams occupy the same volume. | Crossing Lancer beams mixed with Needle bursts. Beam durations, warning timings, and wave clear rule inferred. |
| 03:12 | Larger armored spherical target ahead, accompanied by a bead stream and bright damage effects. | Armored target visual vocabulary informs Gatekeeper; cannot establish its exact role or phase threshold from this frame. |
| 04:00 | Denser curved/radial bead formations and larger star-shaped threats. | Spindle rings, Scythe shards, and Comet pressure. Curvature and exact trajectories are not reproduced from a single frame. |
| 04:48 | Armored spherical target is again ahead, with a sparse downward bead stream. | Recovery breaks between denser attacks; timing inferred. |
| 05:36 | Crossing red lines overlap with scattered bead streams around the player. | Late laser crossweave encounter, two concurrent attackers. |
| 06:24 | Armored target above the center, mixed close bead threats and a smaller glowing source below. | Final-approach mixture before Gatekeeper. Ordering is an adaptation, not verified source wave boundaries. |
| 07:12 | Large central target with dense radial red lines and beads; bright central effect. | Final escalation cue. The implementation uses readable gapped rings/fans instead of copying unmeasured dense radial hazards. |
| 07:32 | Xortex title and start prompt are visible again. | HandFleet results/retry replaces this presentation. |

## Authored encounter sheet (HandFleet combat-clock times)

Times below are authored implementation values, **not claimed source-video timestamps**. Each combat stage lasts at least 25 seconds and then waits for all scheduled enemies to be destroyed. Spawn times are relative to stage entry; when the six-enemy cap is occupied an event waits, without being skipped. All positions are local metres around the calibrated play-space origin.

| Stage | Scheduled family order | Relative spawn times | Placement and movement | Exit |
|---|---|---|---|---|
| Flight/laser training | Needle repetition | Every 4 s | Frontal, harmless practice, half bullet speed | 25 s |
| Optional docking | Needle stationary practice | Every 8 s | Non-attacking targets | 25 s or Skip |
| Approach | Needle ×3, repeated | 0, 1.8, 3.6; 12, 13.8, 15.6 s | -35°, 0°, +35°; shallow lateral drift | 25 s + clear |
| Crossing lanes | Needle, Lancer, Needle, Lancer; repeated | 0–5.4; 12–17.4 s in 1.8-s steps | -100° to +100° arc; second group rotated +30° | 25 s + clear |
| Radial pressure | Spindle, Scythe, Comet; repeated | 0–3.6; 12–15.6 s | Surrounding arc, 0.2-m lateral paths | 25 s + clear |
| Orbital swarm | Spindle, Needle, Scythe, Comet; repeated | 0–5.4; 12–17.4 s | Alternating depth/height, lateral paths | 25 s + clear |
| Laser crossweave | Lancer, Needle, Lancer, Spindle; repeated | 0–5.4; 12–17.4 s | Opposing attack directions, locked beam warning | 25 s + clear |
| Final approach | Scythe, Comet, Spindle, Lancer, Needle; repeated | 0–7.2; 12–19.2 s | Wider mixed formation, staggered attack groups | 25 s + clear |
| Gatekeeper | One boss | 0 s | 2-m frontal distance, slow 0.4-m lateral orbit | Defeat |

Five-second rests follow the six regular encounters. Pause permits an indefinite rest. This produces a roughly four-minute minimum mission; actual length increases with clear time and boss damage uptime.

## Boss and retained HandFleet rules

Boss health 600; inferred phase thresholds 65% / 30%. Phase 1: committed three-shot bursts. Phase 2: gapped seven-projectile rings. Phase 3: five-lane fans with shorter recovery. Phase changes retire the boss's active projectiles and pause attacks for 1.5 seconds. These thresholds and attacks follow HandFleet's PDF readability goals; exact Xortex boss scripting is unverified.

Deliberate adaptations: hand tracking and optional Atlas fusion, three shields, stable core-only collision, calibrated reach, recovery freezes, retained score/multiplier, dominant-hand pinch laser, ten ordinary kills per recharge, and capped pressure. Models, effects, and sound are newly authored; no Valve assets are bundled.

The PDF remains the visual source: upright Kestrel, flush mirrored Atlas, ivory armor/graphite joints/cyan core, amber threats, hollow lime pickups, orbital Launch Cradle, and Gatekeeper iris armor. Only Launch Cradle is included.
