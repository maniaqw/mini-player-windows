# Changelog

## 2.5.1 — 2026-11-02

- Fixed a redundant frame limiter that could skip compositor frames and reduce animation updates to approximately 29 FPS on a 60 Hz display.
- Synchronized animations with compositor timestamps, with consistent smoothing across refresh rates and safe recovery after stalls or hiding the player.
- Smoothed the end of width and hover expansion transitions.
- Reused text layouts and progress-ring geometry to reduce allocations during animation.
- Batched Liquid Glass backdrop updates and avoided rebuilding its geometry when only the window position changes.

Validation: all 96 checks passed, including frame cadence, both player designs, media timelines, and Discord IPC. Local measurements on a 60 Hz display showed approximately 58 FPS for Original and 59 FPS for Liquid Glass, with no compositor frames discarded by the application.
