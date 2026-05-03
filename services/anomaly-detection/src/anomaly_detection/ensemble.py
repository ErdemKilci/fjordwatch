"""Ensemble of IsoForest + LSTM autoencoder."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .features import FEATURE_NAMES, FeatureRow, features_to_frame
from .isoforest import IsoForestScorer
from .lstm_ae import LstmAutoencoder
from .lstm_ae import score as lstm_score


@dataclass
class EnsembleResult:
    mmsi: int
    score: float
    iso_score: float
    lstm_score: float
    contributing: dict[str, float]
    model_versions: dict[str, str]


class EnsembleScorer:
    """Weighted blend of IsoForest tabular score and LSTM-AE reconstruction
    error. Returns one :class:`EnsembleResult` per input vessel.

    The weights are deliberately conservative defaults (0.6 IsoForest /
    0.4 LSTM-AE) so the cheap tabular model dominates and the LSTM serves as
    a tie-breaker on trajectory shape.
    """

    def __init__(
        self,
        iso: IsoForestScorer,
        lstm: LstmAutoencoder | None,
        *,
        iso_weight: float = 0.6,
        lstm_weight: float = 0.4,
        iso_version: str = "iso-v1",
        lstm_version: str = "lstm-v1",
    ) -> None:
        if not (0 <= iso_weight <= 1) or not (0 <= lstm_weight <= 1):
            raise ValueError("weights must be in [0, 1]")
        if abs(iso_weight + lstm_weight - 1.0) > 1e-6:
            raise ValueError("weights must sum to 1")
        self._iso = iso
        self._lstm = lstm
        self._iso_weight = iso_weight
        self._lstm_weight = lstm_weight
        self._iso_version = iso_version
        self._lstm_version = lstm_version

    def score(
        self,
        feature_rows: list[FeatureRow],
        sequences: np.ndarray | None,
    ) -> list[EnsembleResult]:
        if not feature_rows:
            return []
        df = features_to_frame(feature_rows)
        iso_scores = self._iso.score(df)

        if self._lstm is not None and sequences is not None and sequences.size > 0:
            lstm_scores = lstm_score(self._lstm, sequences)
            iso_w, lstm_w = self._iso_weight, self._lstm_weight
        else:
            lstm_scores = np.zeros_like(iso_scores)
            iso_w, lstm_w = 1.0, 0.0

        contribs_df = self._iso.feature_contributions(df)

        results: list[EnsembleResult] = []
        for i, row in enumerate(feature_rows):
            blended = float(iso_w * iso_scores[i] + lstm_w * lstm_scores[i])
            contributing = {name: float(contribs_df.iloc[i][name]) for name in FEATURE_NAMES}
            results.append(
                EnsembleResult(
                    mmsi=row.mmsi,
                    score=blended,
                    iso_score=float(iso_scores[i]),
                    lstm_score=float(lstm_scores[i]),
                    contributing=contributing,
                    model_versions={
                        "iso": self._iso_version,
                        "lstm": self._lstm_version,
                    },
                )
            )
        return results

    def save(self, dir_: Path) -> None:
        dir_.mkdir(parents=True, exist_ok=True)
        self._iso.save(dir_ / "isoforest.pkl")
        # LSTM is exported via lstm_ae.export_onnx by the training script.

    @classmethod
    def load(cls, dir_: Path, *, lstm: LstmAutoencoder | None = None) -> EnsembleScorer:
        iso = IsoForestScorer.load(dir_ / "isoforest.pkl")
        return cls(iso=iso, lstm=lstm)
