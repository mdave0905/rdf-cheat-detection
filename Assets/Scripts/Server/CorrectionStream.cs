using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The server-side correction stream of one player and the per-session features
/// computed from it. These definitions must stay identical to
/// analysis/extract_features.py, because the on-screen detector applies the
/// classifier that was trained on the Python features.
/// </summary>
public class CorrectionStream
{
    public const float LargeCorrectionThreshold = 0.05f;

    private struct Sample
    {
        public uint sequence;
        public float magnitude;
        public float x;
        public float z;
    }

    private readonly List<Sample> samples = new List<Sample>();

    /// <summary>Keep only samples from the most recent N ticks of inputs (0 = keep all).</summary>
    public int MaxSpanTicks { get; set; }

    public int Count => samples.Count;
    public float LatestMagnitude => samples.Count > 0 ? samples[samples.Count - 1].magnitude : 0f;

    public void Add(uint sequence, Vector3 correction)
    {
        samples.Add(new Sample
        {
            sequence = sequence,
            magnitude = correction.magnitude,
            x = correction.x,
            z = correction.z
        });
        if (MaxSpanTicks > 0)
        {
            int firstKept = 0;
            while (firstKept < samples.Count && sequence - samples[firstKept].sequence >= MaxSpanTicks) firstKept++;
            if (firstKept > 0) samples.RemoveRange(0, firstKept);
        }
    }

    public void Clear()
    {
        samples.Clear();
    }

    public float DurationSeconds =>
        samples.Count == 0
            ? 0f
            : (samples[samples.Count - 1].sequence - samples[0].sequence + 1) * GameConstants.FixedDeltaTime;

    public SessionFeatures ComputeFeatures()
    {
        SessionFeatures f = new SessionFeatures();
        int n = samples.Count;
        if (n == 0) return f;

        double duration = DurationSeconds;
        double sum = 0, max = 0, correctionMagnitudeSum = 0, cosSum = 0, sinSum = 0;
        int correctionCount = 0, largeCount = 0;
        long intervalTicks = 0;
        uint previousCorrectionSequence = 0;
        float[] sorted = new float[n];

        for (int i = 0; i < n; i++)
        {
            Sample s = samples[i];
            sorted[i] = s.magnitude;
            sum += s.magnitude;
            if (s.magnitude > max) max = s.magnitude;
            if (s.magnitude >= LargeCorrectionThreshold) largeCount++;

            if (s.magnitude > GameConstants.DivergenceThreshold)
            {
                if (correctionCount > 0) intervalTicks += s.sequence - previousCorrectionSequence;
                previousCorrectionSequence = s.sequence;
                correctionCount++;
                correctionMagnitudeSum += s.magnitude;

                double angle = Math.Atan2(s.z, s.x);
                cosSum += Math.Cos(angle);
                sinSum += Math.Sin(angle);
            }
        }

        double mean = sum / n;
        double variance = 0;
        for (int i = 0; i < n; i++)
        {
            double d = samples[i].magnitude - mean;
            variance += d * d;
        }
        variance /= n;

        Array.Sort(sorted);
        int p95Index = Math.Min(n - 1, (int)Math.Round(0.95 * (n - 1), MidpointRounding.ToEven));

        f.CorrectionCount = correctionCount;
        f.CorrectionsPerSecond = duration > 0 ? correctionCount / duration : 0;
        f.MeanMagnitude = mean;
        f.StdMagnitude = Math.Sqrt(variance);
        f.MaxMagnitude = max;
        f.P95Magnitude = sorted[p95Index];
        f.LargeCorrectionRatio = (double)largeCount / n;
        f.MeanCorrectionMagnitude = correctionCount > 0 ? correctionMagnitudeSum / correctionCount : 0;
        f.MeanCorrectionInterval = correctionCount >= 2
            ? intervalTicks * GameConstants.FixedDeltaTime / (correctionCount - 1)
            : duration;
        f.DirectionConsistency = correctionCount > 0
            ? Math.Sqrt(cosSum * cosSum + sinSum * sinSum) / correctionCount
            : 0;
        return f;
    }
}

public struct SessionFeatures
{
    public int CorrectionCount;
    public double CorrectionsPerSecond;
    public double MeanMagnitude;
    public double StdMagnitude;
    public double MaxMagnitude;
    public double P95Magnitude;
    public double LargeCorrectionRatio;
    public double MeanCorrectionMagnitude;
    public double MeanCorrectionInterval;
    public double DirectionConsistency;

    public double Get(string name)
    {
        switch (name)
        {
            case "CorrectionsPerSecond": return CorrectionsPerSecond;
            case "MeanMagnitude": return MeanMagnitude;
            case "StdMagnitude": return StdMagnitude;
            case "MaxMagnitude": return MaxMagnitude;
            case "P95Magnitude": return P95Magnitude;
            case "LargeCorrectionRatio": return LargeCorrectionRatio;
            case "MeanCorrectionMagnitude": return MeanCorrectionMagnitude;
            case "MeanCorrectionInterval": return MeanCorrectionInterval;
            case "DirectionConsistency": return DirectionConsistency;
            default: throw new ArgumentException($"Unknown feature '{name}'");
        }
    }
}
