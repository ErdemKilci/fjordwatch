# 0002. Isolation Forest + LSTM autoencoder ensemble for anomaly scoring

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Erdem Kilci

## Context

Phase 3 (anomaly detection) needs to flag vessels whose recent behaviour
deviates from baseline traffic. The signal lives in two complementary places:
the cheap per-vessel summary statistics over a 6-hour window
(speed mean/std, heading reversals, stop duration, trajectory entropy), and
the trajectory shape itself (a sequence of lat/lon/sog/heading samples).

A purely tabular model misses anomalies that are only visible in the
sequence (e.g., a vessel that maintains plausible aggregates while taking a
zigzag path). A purely sequence model is more expensive and is harder to
explain to a non-ML reviewer.

## Decision

Run an Isolation Forest over an engineered feature vector (in
`features.py`) and a small LSTM autoencoder over a 64-step resampled
trajectory (in `lstm_ae.py`). Blend the two with a default weight of
0.6 IsoForest / 0.4 LSTM-AE in `EnsembleScorer`. Surface per-feature
contributions from the IsoForest path via an ablation approximation so the
"Anomalies" UI can explain why a row is flagged.

## Considered alternatives

- **Pure IsolationForest.** Pros: trivial to deploy, fast, fully explainable. Cons: misses sequence-shape anomalies, e.g., a vessel that enters a loiter pattern while keeping the same average speed.
- **Pure LSTM-AE.** Pros: captures sequence shape directly. Cons: heavier dependency footprint (PyTorch); reconstruction error is not directly explainable; hyperparameter sensitivity for short windows.
- **Transformer encoder over sequences.** Pros: more expressive than LSTM. Cons: overkill for 64-step windows; slower training; harder to export to ONNX cleanly.
- **DBSCAN / k-means clustering.** Pros: simple. Cons: assumes well-separated clusters; weak when anomalies are diffuse outliers; less interpretable than IsoForest's path-length scoring.

## Consequences

- **Positive:** explainability via IsoForest contributions, sequence-shape coverage via LSTM-AE, low compute (one forward pass per vessel per tick), portable artifacts (pickle for IsoForest, ONNX for LSTM-AE).
- **Negative:** two models to retrain and version. Mitigated by `scripts/train.py` fitting both in one run and registering them as a single MLflow run.
- **Follow-ups:** revisit the 0.6/0.4 weights once we have real labelled data; consider a logistic regression meta-learner stacked on top of the two scores.

## References

- Liu, F. T., Ting, K. M., Zhou, Z.-H. *Isolation Forest.* ICDM 2008.
- Malhotra, P. et al. *LSTM-Based Encoder-Decoder for Multi-sensor Anomaly Detection.* ICML AD Workshop 2016.
