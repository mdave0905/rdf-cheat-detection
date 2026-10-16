"""Train and evaluate the three detectors on the session features.

Models (every non-Legitimate session counts as cheating):
  * Threshold rule  - flag a session if MaxMagnitude exceeds the largest value seen in
                      any honest training session (no false alarms on training data).
  * Logistic regression on the standardised features.
  * Decision tree   - depth chosen by 5-fold cross-validation on the training runs
                      (smallest depth within 0.5 percentage points of the best).

Train/test split: runs are split, not sessions. 140 of the 200 run indices are used
for training and 60 for testing, in every one of the 36 conditions. Because run r uses
the same bot path in every condition, splitting by run keeps a path from appearing in
both sets.

Outputs (in --output): classifier_metrics.csv, per_type_detection.csv,
per_condition_errors.csv, test_predictions.csv, tree_rules.txt, logistic_weights.csv,
model_summary.json. With --csharp, also writes the trained tree as C# for the live HUD.
"""

import argparse
import csv
import json
import os
import random
from collections import defaultdict

import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import roc_auc_score
from sklearn.model_selection import GroupKFold
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.tree import DecisionTreeClassifier, export_text

FEATURES = [
    "CorrectionsPerSecond", "MeanMagnitude", "StdMagnitude", "MaxMagnitude",
    "P95Magnitude", "LargeCorrectionRatio", "MeanCorrectionMagnitude",
    "MeanCorrectionInterval", "DirectionConsistency",
]
FEATURE_TEXT = {
    "CorrectionsPerSecond": "divergent inputs per second",
    "MeanMagnitude": "mean divergence (m)",
    "StdMagnitude": "std. of divergence (m)",
    "MaxMagnitude": "largest divergence (m)",
    "P95Magnitude": "95th percentile divergence (m)",
    "LargeCorrectionRatio": "share of inputs >= 0.05 m",
    "MeanCorrectionMagnitude": "mean divergence of divergent inputs (m)",
    "MeanCorrectionInterval": "mean time between divergent inputs (s)",
    "DirectionConsistency": "direction consistency R",
}
PLAYER_TYPES = ["Legitimate", "SpeedHack", "PositionHack", "InputReplay"]
SPLIT_SEED = 2024
TEST_RUNS = 60
DEPTHS = range(1, 7)


def load(path):
    with open(path, newline="", encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    for row in rows:
        row["Run"] = int(row["Run"])
        row["LatencyMs"] = float(row["LatencyMs"])
        row["PacketLossPercent"] = float(row["PacketLossPercent"])
        row["x"] = [float(row[f]) for f in FEATURES]
        row["y"] = int(row["PlayerType"] != "Legitimate")
    return rows


def split_runs(rows):
    runs = sorted({row["Run"] for row in rows})
    shuffled = runs[:]
    random.Random(SPLIT_SEED).shuffle(shuffled)
    test_runs = set(shuffled[:min(TEST_RUNS, len(runs) * 3 // 10)])
    train = [r for r in rows if r["Run"] not in test_runs]
    test = [r for r in rows if r["Run"] in test_runs]
    return train, test, sorted(test_runs)


def confusion(y, p):
    y, p = np.asarray(y), np.asarray(p)
    tp = int(((y == 1) & (p == 1)).sum())
    tn = int(((y == 0) & (p == 0)).sum())
    fp = int(((y == 0) & (p == 1)).sum())
    fn = int(((y == 1) & (p == 0)).sum())
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / (tp + fn) if tp + fn else 0.0
    return {
        "TP": tp, "TN": tn, "FP": fp, "FN": fn,
        "Accuracy": (tp + tn) / len(y),
        "DetectionRate": recall,
        "FalsePositiveRate": fp / (fp + tn) if fp + tn else 0.0,
        "Precision": precision,
        "F1": 2 * precision * recall / (precision + recall) if precision + recall else 0.0,
    }


def choose_depth(X, y, groups):
    scores = {}
    for depth in DEPTHS:
        fold_scores = []
        for tr, va in GroupKFold(n_splits=5).split(X, y, groups):
            tree = DecisionTreeClassifier(max_depth=depth, min_samples_leaf=5, random_state=0)
            tree.fit(X[tr], y[tr])
            fold_scores.append(float((tree.predict(X[va]) == y[va]).mean()))
        scores[depth] = float(np.mean(fold_scores))
    best = max(scores.values())
    chosen = min(d for d, s in scores.items() if s >= best - 0.005)
    return chosen, scores


def tree_paths(tree):
    """List of (conditions, predicted_class) for every leaf, conditions as (feature, op, threshold)."""
    t = tree.tree_
    out = []

    def walk(node, conditions):
        if t.children_left[node] == -1:
            out.append((conditions, int(np.argmax(t.value[node][0]))))
            return
        name = FEATURES[t.feature[node]]
        threshold = float(t.threshold[node])
        walk(t.children_left[node], conditions + [(name, "<=", threshold)])
        walk(t.children_right[node], conditions + [(name, ">", threshold)])

    walk(0, [])
    return out


def simplify(conditions):
    """Keep only the tightest bound per feature and direction."""
    bounds = {}
    for name, op, threshold in conditions:
        key = (name, op)
        if key not in bounds:
            bounds[key] = threshold
        else:
            bounds[key] = min(bounds[key], threshold) if op == "<=" else max(bounds[key], threshold)
    return [(name, op, bounds[(name, op)]) for name, op in bounds]


def rule_text(conditions):
    return " and ".join(f"{FEATURE_TEXT[n]} {'<=' if op == '<=' else '>'} {t:.3g}" for n, op, t in simplify(conditions))


def write_csharp(tree, path, test_metrics):
    t = tree.tree_
    lines = []

    def emit(node, indent, conditions):
        pad = "    " * indent
        if t.children_left[node] == -1:
            cheat = int(np.argmax(t.value[node][0])) == 1
            if cheat:
                lines.append(f'{pad}reason = "{rule_text(conditions)}";')
                lines.append(f"{pad}return true;")
            else:
                lines.append(f'{pad}reason = "";')
                lines.append(f"{pad}return false;")
            return
        name = FEATURES[t.feature[node]]
        threshold = float(t.threshold[node])
        lines.append(f"{pad}if (f.{name} <= {threshold!r})")
        lines.append(f"{pad}{{")
        emit(t.children_left[node], indent + 1, conditions + [(name, "<=", threshold)])
        lines.append(f"{pad}}}")
        lines.append(f"{pad}else")
        lines.append(f"{pad}{{")
        emit(t.children_right[node], indent + 1, conditions + [(name, ">", threshold)])
        lines.append(f"{pad}}}")

    emit(0, 2, [])
    cheat_rules = [rule_text(c) for c, cls in tree_paths(tree) if cls == 1]
    description = "Flags cheating if: " + "  OR  ".join(cheat_rules) if cheat_rules else "Never flags."
    summary = (f"Test set: {test_metrics['DetectionRate'] * 100:.1f}% of cheat sessions detected, "
               f"{test_metrics['FalsePositiveRate'] * 100:.1f}% of honest sessions flagged.")
    source = [
        "// <auto-generated> by analysis/classify_sessions.py. Do not edit by hand.",
        "// The decision tree trained on the batch experiment, applied by the HUD to the",
        "// last 30 s of server-side divergence data (same features as the training sessions).",
        "public static class TrainedDetector",
        "{",
        "    public const bool IsTrained = true;",
        f"    public const string Description = {json.dumps(description)};",
        f"    public const string TestPerformance = {json.dumps(summary)};",
        "",
        "    public static bool IsCheating(SessionFeatures f, out string reason)",
        "    {",
        *lines,
        "    }",
        "}",
        "",
    ]
    with open(path, "w", encoding="utf-8", newline="\n") as stream:
        stream.write("\n".join(source))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--features", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--csharp", help="Write the trained tree to this .cs file")
    args = parser.parse_args()
    os.makedirs(args.output, exist_ok=True)

    rows = load(args.features)
    train, test, test_runs = split_runs(rows)
    X_train = np.array([r["x"] for r in train])
    y_train = np.array([r["y"] for r in train])
    X_test = np.array([r["x"] for r in test])
    y_test = np.array([r["y"] for r in test])
    groups = np.array([r["Run"] for r in train])

    max_index = FEATURES.index("MaxMagnitude")
    threshold = float(max(r["x"][max_index] for r in train if r["y"] == 0))

    logistic = make_pipeline(StandardScaler(), LogisticRegression(max_iter=10000))
    logistic.fit(X_train, y_train)

    depth, cv_scores = choose_depth(X_train, y_train, groups)
    tree = DecisionTreeClassifier(max_depth=depth, min_samples_leaf=5, random_state=0)
    tree.fit(X_train, y_train)

    scores = {
        "Threshold": X_test[:, max_index],
        "LogisticRegression": logistic.predict_proba(X_test)[:, 1],
        "DecisionTree": tree.predict_proba(X_test)[:, 1],
    }
    predictions = {
        "Threshold": (X_test[:, max_index] > threshold).astype(int),
        "LogisticRegression": logistic.predict(X_test),
        "DecisionTree": tree.predict(X_test),
    }

    metric_rows = []
    for name in predictions:
        m = confusion(y_test, predictions[name])
        m["AUC"] = float(roc_auc_score(y_test, scores[name]))
        metric_rows.append({"Model": name, **m})
    with open(os.path.join(args.output, "classifier_metrics.csv"), "w", newline="", encoding="utf-8") as s:
        w = csv.DictWriter(s, fieldnames=list(metric_rows[0].keys()))
        w.writeheader()
        w.writerows(metric_rows)

    # Detection (or false-alarm) rate per player type, and errors per condition.
    per_type, per_condition = [], []
    for name, pred in predictions.items():
        for ptype in PLAYER_TYPES:
            idx = [i for i, r in enumerate(test) if r["PlayerType"] == ptype]
            flagged = int(sum(pred[i] for i in idx))
            per_type.append({"Model": name, "PlayerType": ptype, "Sessions": len(idx), "Flagged": flagged,
                             "FlaggedRate": flagged / len(idx) if idx else 0.0})
        cells = defaultdict(list)
        for i, r in enumerate(test):
            cells[(r["PlayerType"], r["LatencyMs"], r["PacketLossPercent"])].append(i)
        for (ptype, lat, loss), idx in sorted(cells.items(), key=lambda kv: (PLAYER_TYPES.index(kv[0][0]), kv[0][1], kv[0][2])):
            errors = int(sum(pred[i] != test[i]["y"] for i in idx))
            per_condition.append({"Model": name, "PlayerType": ptype, "LatencyMs": lat, "PacketLossPercent": loss,
                                  "Sessions": len(idx), "Errors": errors})
    for filename, data in (("per_type_detection.csv", per_type), ("per_condition_errors.csv", per_condition)):
        with open(os.path.join(args.output, filename), "w", newline="", encoding="utf-8") as s:
            w = csv.DictWriter(s, fieldnames=list(data[0].keys()))
            w.writeheader()
            w.writerows(data)

    with open(os.path.join(args.output, "test_predictions.csv"), "w", newline="", encoding="utf-8") as s:
        fields = ["SessionId", "PlayerType", "LatencyMs", "PacketLossPercent", "Run", "IsCheat"]
        fields += [f"{n}Prediction" for n in predictions] + [f"{n}Score" for n in predictions]
        w = csv.DictWriter(s, fieldnames=fields)
        w.writeheader()
        for i, r in enumerate(test):
            out = {"SessionId": r["SessionId"], "PlayerType": r["PlayerType"], "LatencyMs": r["LatencyMs"],
                   "PacketLossPercent": r["PacketLossPercent"], "Run": r["Run"], "IsCheat": r["y"]}
            for n in predictions:
                out[f"{n}Prediction"] = int(predictions[n][i])
                out[f"{n}Score"] = float(scores[n][i])
            w.writerow(out)

    paths = tree_paths(tree)
    with open(os.path.join(args.output, "tree_rules.txt"), "w", encoding="utf-8") as s:
        s.write(f"Decision tree, depth {depth} (cross-validated accuracy by depth: "
                + ", ".join(f"{d}: {v:.4f}" for d, v in cv_scores.items()) + ")\n\n")
        s.write(export_text(tree, feature_names=FEATURES, decimals=3))
        s.write("\nLeaves that flag cheating:\n")
        for conditions, cls in paths:
            if cls == 1:
                s.write(f"  - {rule_text(conditions)}\n")

    lr = logistic.named_steps["logisticregression"]
    with open(os.path.join(args.output, "logistic_weights.csv"), "w", newline="", encoding="utf-8") as s:
        w = csv.writer(s)
        w.writerow(["Feature", "StandardisedWeight"])
        w.writerow(["(intercept)", float(lr.intercept_[0])])
        for name, weight in zip(FEATURES, lr.coef_[0]):
            w.writerow([name, float(weight)])

    tree_metrics = next(m for m in metric_rows if m["Model"] == "DecisionTree")
    summary = {
        "sessions": len(rows),
        "trainSessions": len(train),
        "testSessions": len(test),
        "testRuns": test_runs,
        "splitSeed": SPLIT_SEED,
        "thresholdMaxMagnitude": threshold,
        "treeDepth": depth,
        "treeLeaves": int(tree.get_n_leaves()),
        "treeCvAccuracy": cv_scores,
        "treeFeaturesUsed": sorted({FEATURES[f] for f in tree.tree_.feature if f >= 0}),
        "treeThresholds": [float(t) for f, t in zip(tree.tree_.feature, tree.tree_.threshold) if f >= 0],
        "treeCheatRules": [rule_text(c) for c, cls in paths if cls == 1],
    }
    with open(os.path.join(args.output, "model_summary.json"), "w", encoding="utf-8") as s:
        json.dump(summary, s, indent=2)

    if args.csharp:
        write_csharp(tree, args.csharp, tree_metrics)

    if all("LiveVerdict" in r for r in rows):
        offline = tree.predict(np.array([r["x"] for r in rows]))
        live = np.array([int(r["LiveVerdict"]) for r in rows])
        disagreements = int((offline != live).sum())
        summary["liveVerdictDisagreements"] = disagreements
        with open(os.path.join(args.output, "model_summary.json"), "w", encoding="utf-8") as s:
            json.dump(summary, s, indent=2)
        print(f"C# live detector vs Python tree on all {len(rows)} sessions: {disagreements} disagreements")

    for m in metric_rows:
        print(f"{m['Model']:<20} detect {m['DetectionRate']:.3f}  FPR {m['FalsePositiveRate']:.3f}  "
              f"acc {m['Accuracy']:.3f}  AUC {m['AUC']:.3f}  (TP {m['TP']} FP {m['FP']} FN {m['FN']} TN {m['TN']})")
    print(f"Threshold: MaxMagnitude > {threshold:.3f} m; tree depth {depth}; rules: {summary['treeCheatRules']}")


if __name__ == "__main__":
    main()
