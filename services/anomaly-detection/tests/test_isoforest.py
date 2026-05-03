from __future__ import annotations

import numpy as np
import pandas as pd

from anomaly_detection.features import FEATURE_NAMES
from anomaly_detection.isoforest import IsoForestScorer


def test_score_in_unit_interval() -> None:
    rng = np.random.default_rng(0)
    df = pd.DataFrame(rng.normal(size=(64, len(FEATURE_NAMES))), columns=list(FEATURE_NAMES))
    iso = IsoForestScorer(random_state=0).fit(df)
    s = iso.score(df)
    assert s.shape == (64,)
    assert np.all(s >= 0.0) and np.all(s <= 1.0)


def test_anomalous_row_scores_higher_than_typical() -> None:
    rng = np.random.default_rng(0)
    base = rng.normal(loc=0.0, scale=0.5, size=(256, len(FEATURE_NAMES)))
    df_train = pd.DataFrame(base, columns=list(FEATURE_NAMES))
    iso = IsoForestScorer(random_state=0).fit(df_train)

    typical = pd.DataFrame([base.mean(axis=0)], columns=list(FEATURE_NAMES))
    anomalous = pd.DataFrame([base.mean(axis=0) + 6.0], columns=list(FEATURE_NAMES))
    assert iso.score(anomalous)[0] > iso.score(typical)[0]


def test_save_and_load_round_trips(tmp_path) -> None:
    rng = np.random.default_rng(0)
    df = pd.DataFrame(rng.normal(size=(64, len(FEATURE_NAMES))), columns=list(FEATURE_NAMES))
    iso = IsoForestScorer(random_state=0).fit(df)
    path = tmp_path / "iso.pkl"
    iso.save(path)
    loaded = IsoForestScorer.load(path)
    np.testing.assert_allclose(iso.score(df), loaded.score(df))
