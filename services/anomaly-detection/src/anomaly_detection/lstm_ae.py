"""LSTM autoencoder over resampled trajectories.

Inputs are tensors of shape ``(batch, T, 4)`` where T is the resampled step
count and the four channels are normalized longitude, latitude, speed, and
heading. The autoencoder reconstructs the input; reconstruction error per
sample becomes the anomaly score, normalized to ``[0, 1]`` against an
empirical distribution stored at training time.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd
import torch
from torch import nn

T_DEFAULT = 64
CHANNELS = 4  # lon, lat, sog, heading
HIDDEN = 32


@dataclass
class TrainingSummary:
    epochs: int
    final_loss: float
    score_p50: float
    score_p90: float
    score_p99: float


class LstmAutoencoder(nn.Module):
    def __init__(self, *, channels: int = CHANNELS, hidden: int = HIDDEN, sequence_length: int = T_DEFAULT) -> None:
        super().__init__()
        self.sequence_length = sequence_length
        self.encoder = nn.LSTM(channels, hidden, batch_first=True)
        self.decoder = nn.LSTM(hidden, hidden, batch_first=True)
        self.output_head = nn.Linear(hidden, channels)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        _, (h, _) = self.encoder(x)
        # Repeat the encoder's hidden state across the sequence for the decoder.
        decoder_input = h[-1].unsqueeze(1).repeat(1, self.sequence_length, 1)
        decoded, _ = self.decoder(decoder_input)
        return self.output_head(decoded)


def resample_trajectory(df: pd.DataFrame, *, target_steps: int = T_DEFAULT) -> np.ndarray:
    """Resample a per-vessel position frame to a fixed-length tensor.

    Linear interpolation across timestamps; missing speed/heading filled with 0.
    Returns a ``(target_steps, CHANNELS)`` float32 array.
    """
    if df.empty:
        return np.zeros((target_steps, CHANNELS), dtype=np.float32)
    df = df.sort_values("ts").reset_index(drop=True)
    if len(df) == 1:
        single = np.array(
            [
                df["longitude"].iloc[0],
                df["latitude"].iloc[0],
                float(df["sog_knots"].iloc[0] or 0.0),
                float(df["heading_deg"].iloc[0] or 0.0),
            ],
            dtype=np.float32,
        )
        return np.tile(single, (target_steps, 1))

    timestamps = df["ts"].astype("int64").to_numpy(dtype=np.float64)
    t_min, t_max = timestamps.min(), timestamps.max()
    if t_max == t_min:
        t_max = t_min + 1.0
    target_t = np.linspace(t_min, t_max, target_steps)

    def interp(col: str, default: float) -> np.ndarray:
        v = df[col].to_numpy(dtype=np.float32)
        v = np.where(np.isnan(v), default, v)
        return np.interp(target_t, timestamps, v).astype(np.float32)

    lon = interp("longitude", 0.0)
    lat = interp("latitude", 0.0)
    sog = interp("sog_knots", 0.0)
    heading = interp("heading_deg", 0.0)

    arr = np.stack([lon, lat, sog, heading], axis=-1)
    return _normalize(arr)


def _normalize(arr: np.ndarray) -> np.ndarray:
    """Per-channel normalize to a roughly comparable scale.

    Latitude/longitude land in [0, 1]-ish via a fixed Norwegian-coast bbox;
    speed in knots / 30; heading / 360.
    """
    out = arr.copy()
    out[:, 0] = (out[:, 0] - 4.0) / 28.0  # 4 .. 32 covers Norwegian coast
    out[:, 1] = (out[:, 1] - 58.0) / 14.0  # 58 .. 72
    out[:, 2] = out[:, 2] / 30.0
    out[:, 3] = out[:, 3] / 360.0
    return out


def train(
    sequences: np.ndarray,
    *,
    epochs: int = 5,
    learning_rate: float = 1e-3,
    seed: int = 42,
) -> tuple[LstmAutoencoder, TrainingSummary]:
    """Train on a stack of sequences shaped ``(N, T, CHANNELS)``."""
    if sequences.ndim != 3 or sequences.shape[1] == 0 or sequences.shape[2] != CHANNELS:
        raise ValueError(f"unexpected training shape {sequences.shape}")

    torch.manual_seed(seed)
    model = LstmAutoencoder(sequence_length=sequences.shape[1])
    optimizer = torch.optim.Adam(model.parameters(), lr=learning_rate)
    loss_fn = nn.MSELoss()

    x = torch.from_numpy(sequences.astype(np.float32))
    final_loss = float("nan")
    for _ in range(epochs):
        model.train()
        optimizer.zero_grad()
        recon = model(x)
        loss = loss_fn(recon, x)
        loss.backward()
        optimizer.step()
        final_loss = float(loss.detach().item())

    model.eval()
    with torch.no_grad():
        errors = ((model(x) - x) ** 2).mean(dim=(1, 2)).cpu().numpy()
    summary = TrainingSummary(
        epochs=epochs,
        final_loss=final_loss,
        score_p50=float(np.percentile(errors, 50)),
        score_p90=float(np.percentile(errors, 90)),
        score_p99=float(np.percentile(errors, 99)),
    )
    return model, summary


def score(model: LstmAutoencoder, sequences: np.ndarray) -> np.ndarray:
    """Per-sample reconstruction error normalized to ``[0, 1]`` via tanh.

    The tanh squashing matches the IsoForest output range so the ensemble can
    take a simple weighted mean.
    """
    model.eval()
    with torch.no_grad():
        x = torch.from_numpy(sequences.astype(np.float32))
        recon = model(x)
        errors = ((recon - x) ** 2).mean(dim=(1, 2)).cpu().numpy()
    return np.tanh(errors).astype(np.float32)


def export_onnx(model: LstmAutoencoder, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    dummy = torch.zeros(1, model.sequence_length, CHANNELS, dtype=torch.float32)
    torch.onnx.export(
        model,
        (dummy,),
        str(path),
        input_names=["sequence"],
        output_names=["reconstruction"],
        dynamic_axes={"sequence": {0: "batch"}, "reconstruction": {0: "batch"}},
        opset_version=17,
    )
