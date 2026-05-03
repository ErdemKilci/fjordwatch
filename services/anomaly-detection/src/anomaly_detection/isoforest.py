"""Isolation Forest scorer over the engineered tabular features."""

from __future__ import annotations

import pickle
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from sklearn.ensemble import IsolationForest

from .features import FEATURE_NAMES


class IsoForestScorer:
    """Wraps :class:`sklearn.ensemble.IsolationForest` with a stable score
    interface that returns values in ``[0, 1]`` where 1 is most anomalous.

    sklearn's raw ``decision_function`` returns a value where lower means
    more anomalous and the absolute scale depends on the trained forest, so
    we normalize through the inverse: ``score = 1 / (1 + exp(decision))``.
    """

    def __init__(
        self,
        *,
        n_estimators: int = 100,
        contamination: float | str = "auto",
        random_state: int = 42,
    ) -> None:
        self._model = IsolationForest(
            n_estimators=n_estimators,
            contamination=contamination,
            random_state=random_state,
            n_jobs=1,
        )
        self._fitted = False

    def fit(self, df: pd.DataFrame) -> "IsoForestScorer":
        x = self._extract(df)
        self._model.fit(x)
        self._fitted = True
        return self

    def score(self, df: pd.DataFrame) -> np.ndarray:
        if not self._fitted:
            raise RuntimeError("IsoForestScorer must be fitted before scoring")
        x = self._extract(df)
        decision = self._model.decision_function(x)
        return _decision_to_score(decision)

    def feature_contributions(self, df: pd.DataFrame) -> pd.DataFrame:
        """Approximate per-feature contribution by leaving each feature at
        its training median and rescoring; the delta indicates how much that
        feature pushed the row toward "anomalous".

        This is the "ablation" approximation; a full SHAP path-length
        attribution would be more precise but adds a heavy dependency.
        """
        if not self._fitted:
            raise RuntimeError("IsoForestScorer must be fitted before scoring")
        x = self._extract(df)
        baseline_score = _decision_to_score(self._model.decision_function(x))
        contributions = np.zeros_like(x, dtype=np.float32)
        for j in range(x.shape[1]):
            ablated = x.copy()
            ablated[:, j] = np.median(x[:, j])
            ablated_score = _decision_to_score(self._model.decision_function(ablated))
            contributions[:, j] = baseline_score - ablated_score
        return pd.DataFrame(contributions, columns=list(FEATURE_NAMES), index=df.index)

    def save(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("wb") as f:
            pickle.dump({"model": self._model, "fitted": self._fitted}, f)

    @classmethod
    def load(cls, path: Path) -> "IsoForestScorer":
        with path.open("rb") as f:
            blob: dict[str, Any] = pickle.load(f)  # noqa: S301 (trusted artifact)
        scorer = cls()
        scorer._model = blob["model"]
        scorer._fitted = bool(blob["fitted"])
        return scorer

    @staticmethod
    def _extract(df: pd.DataFrame) -> np.ndarray:
        missing = [c for c in FEATURE_NAMES if c not in df.columns]
        if missing:
            raise ValueError(f"missing feature columns: {missing}")
        return df[list(FEATURE_NAMES)].to_numpy(dtype=np.float32)


def _decision_to_score(decision: np.ndarray) -> np.ndarray:
    """Map sklearn's signed decision to ``[0, 1]`` with 1 = most anomalous."""
    return 1.0 / (1.0 + np.exp(decision))
