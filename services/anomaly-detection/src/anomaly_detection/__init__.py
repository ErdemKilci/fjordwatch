"""FjordWatch trajectory anomaly detection.

Pipeline:

    Postgres positions
        -> features.compute_features (per-vessel feature row over a window)
            -> ensemble.EnsembleScorer (IsoForest + LSTM-AE)
                -> store.write_anomalies -> Postgres vessel_anomalies
"""

__version__ = "0.1.0"
