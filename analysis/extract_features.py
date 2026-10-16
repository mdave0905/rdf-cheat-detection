"""Turn the server logs of the batch experiment into one feature row per session.

Every log is checked against the session manifest (experiment_sessions.csv) that the
Unity collector writes. A session is only used if:
  * it appears exactly once in the manifest and its log holds a single SessionId,
  * sequence numbers strictly increase (no appended or duplicated runs),
  * the number of rows equals the number of inputs the server logged,
  * no datagram arrived later than its scheduled tick (LateDeliveries == 0).

The feature definitions must match Assets/Scripts/Server/CorrectionStream.cs,
which computes the same features live for the on-screen detector.

Usage:
    python analysis/extract_features.py --input <dataset folder> --output analysis/results
"""

import argparse
import csv
import glob
import math
import os

TICK_SECONDS = 1.0 / 30.0
DIVERGENCE_THRESHOLD = 0.01   # GameConstants.DivergenceThreshold
LARGE_THRESHOLD = 0.05        # CorrectionStream.LargeCorrectionThreshold
MANIFEST = "experiment_sessions.csv"

FEATURES = [
    "CorrectionsPerSecond", "MeanMagnitude", "StdMagnitude", "MaxMagnitude",
    "P95Magnitude", "LargeCorrectionRatio", "MeanCorrectionMagnitude",
    "MeanCorrectionInterval", "DirectionConsistency",
]


def compute_features(sequences, magnitudes, vx, vz):
    """Session features. Mirrors CorrectionStream.ComputeFeatures in C#."""
    n = len(magnitudes)
    duration = (sequences[-1] - sequences[0] + 1) * TICK_SECONDS
    mean = sum(magnitudes) / n
    variance = sum((m - mean) ** 2 for m in magnitudes) / n
    ordered = sorted(magnitudes)
    p95 = ordered[min(n - 1, int(round(0.95 * (n - 1))))]

    correction_idx = [i for i, m in enumerate(magnitudes) if m > DIVERGENCE_THRESHOLD]
    count = len(correction_idx)
    if count >= 2:
        gaps = [sequences[b] - sequences[a] for a, b in zip(correction_idx, correction_idx[1:])]
        mean_interval = sum(gaps) * TICK_SECONDS / (count - 1)
    else:
        mean_interval = duration

    if count:
        cos_sum = sum(math.cos(math.atan2(vz[i], vx[i])) for i in correction_idx)
        sin_sum = sum(math.sin(math.atan2(vz[i], vx[i])) for i in correction_idx)
        consistency = math.sqrt(cos_sum ** 2 + sin_sum ** 2) / count
        mean_correction = sum(magnitudes[i] for i in correction_idx) / count
    else:
        consistency = 0.0
        mean_correction = 0.0

    return {
        "CorrectionCount": count,
        "CorrectionsPerSecond": count / duration,
        "MeanMagnitude": mean,
        "StdMagnitude": math.sqrt(variance),
        "MaxMagnitude": max(magnitudes),
        "P95Magnitude": p95,
        "LargeCorrectionRatio": sum(m >= LARGE_THRESHOLD for m in magnitudes) / n,
        "MeanCorrectionMagnitude": mean_correction,
        "MeanCorrectionInterval": mean_interval,
        "DirectionConsistency": consistency,
    }


def read_manifest(folder):
    path = os.path.join(folder, MANIFEST)
    if not os.path.exists(path):
        raise SystemExit(f"Manifest {path} not found; is this a dataset folder written by the collector?")
    with open(path, newline="", encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    by_id = {}
    for row in rows:
        by_id.setdefault(row["SessionId"], []).append(row)
    return by_id


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="Dataset folder (reconciliation_*.csv + experiment_sessions.csv)")
    parser.add_argument("--output", required=True, help="Output folder")
    args = parser.parse_args()
    os.makedirs(args.output, exist_ok=True)

    manifest = read_manifest(args.input)
    paths = sorted(glob.glob(os.path.join(args.input, "reconciliation_*.csv")))
    features_out, integrity = [], []

    for path in paths:
        with open(path, newline="", encoding="utf-8-sig") as stream:
            rows = list(csv.DictReader(stream))
        file_id = os.path.basename(path)[len("reconciliation_"):-len(".csv")]
        problems = []

        session_ids = {row["SessionId"] for row in rows}
        entries = manifest.get(file_id, [])
        if not rows:
            problems.append("empty log")
        if len(session_ids) > 1 or (rows and file_id not in session_ids):
            problems.append("log holds more than one session id")
        if len(entries) != 1:
            problems.append(f"{len(entries)} manifest rows")

        sequences = [int(row["SequenceNumber"]) for row in rows]
        if any(b <= a for a, b in zip(sequences, sequences[1:])):
            problems.append("sequence numbers not strictly increasing (appended or duplicated run)")

        entry = entries[0] if len(entries) == 1 else None
        if entry is not None:
            if int(entry["RowsLogged"]) != len(rows):
                problems.append(f"{len(rows)} rows but manifest says {entry['RowsLogged']}")
            if int(entry["LateDeliveries"]) != 0:
                problems.append(f"{entry['LateDeliveries']} late deliveries")

        integrity.append({
            "SessionId": file_id,
            "Rows": len(rows),
            "Status": "ok" if not problems else "excluded",
            "Problems": "; ".join(problems),
        })
        if problems:
            continue

        magnitudes = [float(row["Magnitude"]) for row in rows]
        vx = [float(row["CorrectionVectorX"]) for row in rows]
        vz = [float(row["CorrectionVectorZ"]) for row in rows]
        inputs_sent = int(entry["InputsSent"])
        states_sent = int(entry["StatesSent"])

        record = {
            "SessionId": file_id,
            "PlayerType": entry["PlayerType"],
            "LatencyMs": float(entry["LatencyMs"]),
            "EffectiveOneWayDelayMs": float(entry["EffectiveOneWayDelayMs"]),
            "PacketLossPercent": float(entry["PacketLossPercent"]),
            "Run": int(file_id.rsplit("_", 1)[1]),
            "MovementSeed": int(entry["MovementSeed"]),
            "Rows": len(rows),
            "DurationSeconds": (sequences[-1] - sequences[0] + 1) * TICK_SECONDS,
            "MeasuredInputLossPercent": 100.0 * int(entry["InputsDropped"]) / inputs_sent,
            "MeasuredStateLossPercent": 100.0 * int(entry["StatesDropped"]) / states_sent if states_sent else 0.0,
            "ClientCorrections": int(entry["ClientCorrections"]),
        }
        record.update(compute_features(sequences, magnitudes, vx, vz))

        # Cross-check against the features the C# live detector computed for the same session.
        if "LiveMeanCorrectionMagnitude" in entry:
            worst = 0.0
            for name in FEATURES:
                live, offline = float(entry["Live" + name]), record[name]
                worst = max(worst, abs(live - offline) / max(1.0, abs(offline)))
            record["LiveFeatureMaxRelDiff"] = worst
            record["LiveVerdict"] = int(entry["LiveVerdict"])
        features_out.append(record)

    orphan_manifest = sorted(set(manifest) - {row["SessionId"] for row in integrity})
    for session_id in orphan_manifest:
        integrity.append({"SessionId": session_id, "Rows": 0, "Status": "excluded", "Problems": "manifest row without log file"})

    if not features_out:
        raise SystemExit("No usable sessions found.")

    with open(os.path.join(args.output, "session_features.csv"), "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(features_out[0].keys()))
        writer.writeheader()
        writer.writerows(features_out)

    with open(os.path.join(args.output, "integrity_report.csv"), "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=["SessionId", "Rows", "Status", "Problems"])
        writer.writeheader()
        writer.writerows(integrity)

    excluded = sum(row["Status"] != "ok" for row in integrity)
    print(f"{len(paths)} logs, {len(features_out)} usable sessions, {excluded} excluded -> {args.output}")
    if "LiveFeatureMaxRelDiff" in features_out[0]:
        worst = max(r["LiveFeatureMaxRelDiff"] for r in features_out)
        print(f"C# live features vs Python features: largest relative difference {worst:.2e}")


if __name__ == "__main__":
    main()
