"""Export the data behind the dashboard (analysis/dashboard/index.html) into dashboard_data.js.

Everything shown on the dashboard comes from this file, which is built only from
  results/session_features.csv, results/integrity_report.csv, results/condition_summary.csv
  results/classifiers/*   (classify_sessions.py)
  the raw per-input logs in the dataset folder (for the session explorer only)

Usage:
  python export_dashboard_data.py --dataset "%USERPROFILE%\\AppData\\LocalLow\\DefaultCompany\\ThesisPrototype\\dataset_v2"
"""

import argparse
import csv
import json
import os

from sklearn.metrics import roc_curve

HERE = os.path.dirname(os.path.abspath(__file__))
RESULTS = os.path.join(HERE, "results")
CLASSIFIERS = os.path.join(RESULTS, "classifiers")
DASHBOARD = os.path.join(HERE, "dashboard")
DEFAULT_DATASET = os.path.join(os.path.expanduser("~"), "AppData", "LocalLow", "DefaultCompany", "ThesisPrototype", "dataset_v2")

TYPES = ["Legitimate", "SpeedHack", "PositionHack", "InputReplay"]
MODELS = ["Threshold", "LogisticRegression", "DecisionTree"]
FEATURES = [
    "CorrectionsPerSecond", "MeanMagnitude", "StdMagnitude", "MaxMagnitude", "P95Magnitude",
    "LargeCorrectionRatio", "MeanCorrectionMagnitude", "MeanCorrectionInterval", "DirectionConsistency",
]
EXPLORER_RUNS_PER_CONDITION = 3


def read_csv(path):
    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def to_number(value):
    try:
        number = float(value)
    except (TypeError, ValueError):
        return value
    return int(number) if number.is_integer() and abs(number) < 1e15 else number


def load_session(dataset, session_id):
    rows = read_csv(os.path.join(dataset, f"reconciliation_{session_id}.csv"))
    seq, cx, cz, ax, az, mag = [], [], [], [], [], []
    for row in rows:
        seq.append(int(row["SequenceNumber"]))
        cx.append(round(float(row["ClaimedPositionX"]), 3))
        cz.append(round(float(row["ClaimedPositionZ"]), 3))
        ax.append(round(float(row["AuthoritativePositionX"]), 3))
        az.append(round(float(row["AuthoritativePositionZ"]), 3))
        mag.append(round(float(row["Magnitude"]), 4))
    return {"seq": seq, "cx": cx, "cz": cz, "ax": ax, "az": az, "mag": mag}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dataset", default=DEFAULT_DATASET)
    args = parser.parse_args()

    features = read_csv(os.path.join(RESULTS, "session_features.csv"))
    integrity = read_csv(os.path.join(RESULTS, "integrity_report.csv"))
    conditions = [{k: to_number(v) for k, v in r.items()} for r in read_csv(os.path.join(RESULTS, "condition_summary.csv"))]
    metrics = [{k: to_number(v) for k, v in r.items()} for r in read_csv(os.path.join(CLASSIFIERS, "classifier_metrics.csv"))]
    per_type = [{k: to_number(v) for k, v in r.items()} for r in read_csv(os.path.join(CLASSIFIERS, "per_type_detection.csv"))]
    predictions = read_csv(os.path.join(CLASSIFIERS, "test_predictions.csv"))
    weights = [{k: to_number(v) for k, v in r.items()} for r in read_csv(os.path.join(CLASSIFIERS, "logistic_weights.csv"))]
    with open(os.path.join(CLASSIFIERS, "model_summary.json"), encoding="utf-8") as handle:
        summary = json.load(handle)

    by_id = {r["SessionId"]: r for r in features}
    pred_by_id = {r["SessionId"]: r for r in predictions}

    # One compact row per session: everything the overview charts need.
    session_columns = ["id", "type", "latency", "loss", "run", "test", "rows", "inputLoss", "stateLoss",
                       "clientCorrections", "divergentInputs"] + FEATURES + [f"pred{m}" for m in MODELS]
    session_rows = []
    for r in features:
        p = pred_by_id.get(r["SessionId"])
        session_rows.append(
            [r["SessionId"], TYPES.index(r["PlayerType"]), to_number(r["LatencyMs"]), to_number(r["PacketLossPercent"]),
             int(r["Run"]), 1 if p else 0, int(r["Rows"]), round(float(r["MeasuredInputLossPercent"]), 3),
             round(float(r["MeasuredStateLossPercent"]), 3), int(r["ClientCorrections"]), int(r["CorrectionCount"])]
            + [round(float(r[f]), 5) for f in FEATURES]
            + [int(p[f"{m}Prediction"]) if p else None for m in MODELS]
        )

    # ROC curves on the test runs.
    y = [int(p["IsCheat"]) for p in predictions]
    roc = {}
    for model in MODELS:
        fpr, tpr, _ = roc_curve(y, [float(p[f"{model}Score"]) for p in predictions])
        roc[model] = {"fpr": [round(v, 5) for v in fpr], "tpr": [round(v, 5) for v in tpr]}

    # Every test-set error of every detector, with the numbers that explain it.
    errors = []
    for p in predictions:
        wrong = [m for m in MODELS if int(p[f"{m}Prediction"]) != int(p["IsCheat"])]
        if wrong:
            f = by_id[p["SessionId"]]
            errors.append({
                "id": p["SessionId"], "type": p["PlayerType"], "latency": to_number(p["LatencyMs"]),
                "loss": to_number(p["PacketLossPercent"]), "run": int(p["Run"]), "wrong": wrong,
                "meanDivergent": round(float(f["MeanCorrectionMagnitude"]), 4),
                "max": round(float(f["MaxMagnitude"]), 4), "R": round(float(f["DirectionConsistency"]), 4),
            })

    # Session explorer: the first few test runs of every condition plus every session the tree got wrong.
    test_runs = sorted(summary["testRuns"])[:EXPLORER_RUNS_PER_CONDITION]
    explorer_ids = [r["SessionId"] for r in features if int(r["Run"]) in test_runs]
    explorer_ids += [e["id"] for e in errors if "DecisionTree" in e["wrong"] and e["id"] not in explorer_ids]
    explorer = {sid: load_session(args.dataset, sid) for sid in explorer_ids}

    excluded = [r for r in integrity if r["Status"] != "ok"]
    payload = {
        "meta": {
            "sessions": len(features),
            "logs": len(integrity),
            "excluded": len(excluded),
            "excludedReasons": sorted({r["Problems"] for r in excluded}),
            "maxLiveFeatureDiff": max(float(r["LiveFeatureMaxRelDiff"]) for r in features),
            "liveVerdictDisagreements": summary["liveVerdictDisagreements"],
            "trainSessions": summary["trainSessions"],
            "testSessions": summary["testSessions"],
            "testRuns": summary["testRuns"],
            "splitSeed": summary["splitSeed"],
            "types": TYPES,
            "latencies": sorted({to_number(r["LatencyMs"]) for r in features}),
            "losses": sorted({to_number(r["PacketLossPercent"]) for r in features}),
            "effectiveOneWay": {str(to_number(r["LatencyMs"])): round(round(float(r["EffectiveOneWayDelayMs"]) * 0.03) / 0.03, 2) for r in features},
            "sessionTicks": 900,
            "tickRate": 30,
            "divergenceEpsilon": 0.01,
            "largeDivergence": 0.05,
            "arenaHalfSize": 15,
            "explorerRuns": test_runs,
        },
        "sessionColumns": session_columns,
        "sessions": session_rows,
        "conditions": conditions,
        "metrics": metrics,
        "perType": per_type,
        "roc": roc,
        "errors": errors,
        "model": {
            "thresholdMaxMagnitude": summary["thresholdMaxMagnitude"],
            "treeDepth": summary["treeDepth"],
            "treeCvAccuracy": summary["treeCvAccuracy"],
            "treeFeatures": summary["treeFeaturesUsed"],
            "treeThresholds": summary["treeThresholds"],
            "treeRules": summary["treeCheatRules"],
            "logisticWeights": weights,
        },
        "explorer": explorer,
    }

    os.makedirs(DASHBOARD, exist_ok=True)
    for stale in ("dashboard_data.json",):
        path = os.path.join(DASHBOARD, stale)
        if os.path.exists(path):
            os.remove(path)
    out = os.path.join(DASHBOARD, "dashboard_data.js")
    with open(out, "w", encoding="utf-8") as handle:
        handle.write("window.DASHBOARD_DATA = ")
        json.dump(payload, handle, separators=(",", ":"))
        handle.write(";\n")
    print(f"{len(session_rows)} sessions, {len(explorer)} explorer sessions, {len(errors)} test errors "
          f"-> {out} ({os.path.getsize(out) / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
