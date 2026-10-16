using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Where session logs are written. Batch sessions go to a dedicated dataset folder
/// (override with the -outdir command-line argument); interactive sessions go to a
/// separate "manual" folder so they can never mix with experiment data.
/// </summary>
public static class ExperimentPaths
{
    public static string DatasetDirectory
    {
        get
        {
            string fromArgs = CommandLine.GetValue("-outdir");
            string directory = !string.IsNullOrEmpty(fromArgs)
                ? fromArgs
                : Path.Combine(Application.persistentDataPath, "dataset");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string ManualDirectory
    {
        get
        {
            string directory = Path.Combine(Application.persistentDataPath, "manual");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }
}

public static class CommandLine
{
    public static bool HasFlag(string flag)
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static string GetValue(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }
}
