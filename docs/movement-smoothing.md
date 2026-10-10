# Client movement smoothing

The alpha.5 client called `GameTime.UpdateForSync` on every visual frame. In the
installed CoI 0.8.7d implementation that method resets `AbsoluteT`, `RelativeT`,
and `DeltaT` to zero. Ground vehicle positions use `AbsoluteT`; truck wheels use
`RelativeT`. Ships, train cars, excavator cabins, and harvester animations also
use the interpolation clock. This explains the reported combination of smooth
camera/UI movement and vehicles moving in visible steps.

The alpha.5.1 client captures the native synchronized render state after each
authoritative update, then advances the native visual clock between updates.
It preserves the native render step, which can lag the simulation step, and
keeps speed metadata separate from the 100 ms interpolation interval. Late
packets hold the last known pose; the client does not extrapolate simulation
state. Paused or disconnected islands use the native paused render clock.

Replaying a packet still runs native sync and after-sync callbacks, which update
visual endpoints. It skips the ordinary render callback for that synthetic
100 ms update; normal Unity frames render the interpolated result. Rendering
does not schedule extra simulation updates or change server commands, pacing,
saves, checkpoints, or the compatibility revision.

## Evidence and limits

REA artifact evidence:
`ev_88aa3d17e98be8071e0bd21a8816c4aafbd9f02b14a41d387f39d7ac1512f43c`.
Mafi.Core SHA-256:
`6292fe37a4132a60b2dadae16d65e76af2a80b5ed251e10a75ad19676c8f187c`;
MVID `bd02733e-cf8d-4d7f-a22e-5b8fcf8a533c`.
The REA inventory has partial metadata coverage and does not execute or decompile
the assembly. Focused local ILSpy inspection of `GameTime`, `GameRunner`, and
the affected Unity renderers establishes the timing and consumer paths above.
Game binaries and raw decompilation are excluded from source and releases.

All nine regression checks pass against the installed native `GameTime` implementation.
It checks smooth interpolation between updates, speed and pause transitions,
preserved render-step metadata, and bounded behavior when packets are late.
It does not render Unity scenes. Actual visual improvement still needs an
in-game check with moving vehicles at 1–4x, wheels/tracks, and pause/resume.
Network jitter, GPU pose latency, and periodic checkpoint stalls are separate
sources of occasional hitches; this patch does not eliminate them.
