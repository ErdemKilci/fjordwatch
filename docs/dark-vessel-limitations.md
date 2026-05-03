# Dark vessel detection — limitations and scope

This document is mandatory reading before reviewing or referencing the
"dark vessels" overlay on the FjordWatch map. **The label "dark" in this
project means "not correlated with an AIS broadcast in a 500 m / 30-minute
window". It does not mean "rogue", "non-cooperative", or anything legally
or operationally meaningful.**

## What we detect

- The SAR pipeline opens a Sentinel-1 GRD scene, applies a sigma0 dB
  approximation, and runs YOLOv8 inference (currently a placeholder ONNX;
  a real fine-tuned model is a manual step). Output is a list of pixel
  bounding boxes converted to WGS84 centroids via the tile's geotransform.
- The correlator queries `positions` for an AIS broadcast within
  `CORRELATION_RADIUS_M` and `CORRELATION_WINDOW_S` (defaults 500 m / 30 min)
  of each SAR detection. When no AIS row matches, `is_dark = TRUE`.

## Why a "dark" classification is not actionable

1. **Small vessels below the AIS reporting threshold.** Pleasure craft,
   small fishing boats, and many local ferries are not required to carry
   AIS. They will appear dark and that is normal.
2. **AIS dropouts.** Real AIS receivers miss broadcasts; the Kystverket feed
   has gaps measured in minutes near the coast and longer offshore. A dark
   detection is at most weak evidence of a missing broadcast.
3. **SAR false positives.** Sea ice, oil platforms, rocks, breaking waves,
   buoys, and sidelobe artefacts all produce bright SAR returns that can
   fool any object detector. Without manual review or a SAR-specialist
   model, a non-trivial fraction of detections are not vessels at all.
4. **Geolocation error.** The pixel-to-WGS84 conversion treats each tile as
   a flat rectangle. Sentinel-1 GRD scenes are reasonably well
   geo-referenced (~10 m), but tile-edge vessels can drift up to a few
   pixels in either direction.
5. **Temporal mismatch.** A `30-minute` window is a defensible default for
   coastal Norway but is arbitrary; a shorter window misses vessels that
   are between AIS broadcasts, a longer window matches against vessels
   that have moved out of range.

## Honest limitations of this implementation

- **The YOLOv8 model is a placeholder by default.** The shipped service
  returns zero detections until a developer trains and registers a real
  ONNX. The pipeline is correct end-to-end; the model is a manual step.
- **No oil-platform mask.** Real operational systems mask known fixed
  installations from the input. Phase 6 polish wires this against
  Sjøfartsdirektoratet's installation register.
- **No SAR-specific augmentation in training.** The default suggested
  workflow fine-tunes a COCO-pretrained YOLOv8n on a public ship dataset
  (xView, AirBus, HRSC2016). That dataset is mostly optical imagery,
  which transfers poorly to SAR. A SAR-specific dataset (SSDD, HRSID) is
  the right baseline; phase 6 polish migrates.
- **One ground-truth scene is not enough to claim F1 > 0.7.** The phase 4
  acceptance gate is plumbing-correct (the overlay renders, the
  correlator runs, the database is consistent). Quality on a labelled
  scene is a phase 6 polish item.

## What the UI shows

- **Red marker:** SAR detection with no matching AIS in the configured
  window. Tooltip shows confidence and the raw pixel bbox where useful.
- **Blue marker:** SAR detection matched to an AIS broadcast. Tooltip shows
  the matched MMSI, distance in metres, and lag in seconds.
- The overlay is off by default. The "Dark only" toggle filters server-side
  via `?onlyDark=true` so the bundle does not slow down on busy scenes.

## What this project must not be used for

- Law enforcement, customs, or military targeting.
- Any decision that affects a specific vessel or operator.
- Alerting any third party that a vessel is "behaving suspiciously".

This is a research and learning project. The disclaimers in
[`DISCLAIMER.md`](../DISCLAIMER.md) and on the `/about` page apply in full.

## Military vessels

ITU-R M.1371-5 defines ship type **35 (Military)** as a self-classified
category an operator may set on their AIS transponder. When a Norwegian
naval vessel is on a sensitive operation it typically operates with AIS
**off**; when AIS is **on** the broadcast is voluntary, unencrypted, and
publicly receivable on VHF Channels A and B. Public AIS aggregators
(MarineTraffic, VesselFinder, FleetMon) all surface these broadcasts.

FjordWatch renders the Military category in the legend for ITU
completeness. **It does not run any targeted analytics on military
vessels:**

- No per-vessel alerts on type-35 hulls.
- The anomaly detector treats all ship types uniformly; the ensemble
  has no special branch that profiles military patterns.
- The dark-vessel correlator does not specifically pair SAR detections
  with type-35 broadcasts. A SAR detection without an AIS match is
  flagged dark regardless of whether nearby AIS broadcasts include
  type 35.
- The agent's tools (nearest_vessels, vessel_history, recent_anomalies,
  dark_vessels, search_regulations) accept ship-type filters for UI
  ergonomics but do not weight, prioritize, or hide military hulls.

If a deployment of FjordWatch is observed deriving operational
intelligence about Norwegian Armed Forces movements, that is a misuse
of the tool. Refer to:

- Norwegian Security Act (Sikkerhetsloven 2018) for what counts as
  classified information.
- Norwegian Penal Code chapters 17 and 18 (Straffeloven) for the
  espionage and treason offence definitions.
- Forsvaret and Sjøforsvaret guidance on AIS handling.

Public AIS data is not classified by definition (the operator chose to
broadcast), so simply rendering it does not engage these statutes. But
combining FjordWatch outputs with non-public sources to derive
operational patterns about military movements would, and that is
explicitly outside the scope and acceptable use of this project.
