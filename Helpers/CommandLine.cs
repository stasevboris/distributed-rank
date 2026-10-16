using System.Globalization;

namespace DistributedRank;

/// <summary>Parses --key value pairs from command-line arguments.</summary>
internal static class CommandLine
{
    internal static string GetOption(string[] args, string name, string fallback)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return fallback;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
            throw new ArgumentException($"Missing value for {name}.");
        return args[index + 1];
    }

    internal static int GetIntOption(string[] args, string name, int fallback) =>
        int.Parse(GetOption(args, name, fallback.ToString(CultureInfo.InvariantCulture)),
            CultureInfo.InvariantCulture);

    internal static double GetDoubleOption(string[] args, string name, double fallback) =>
        double.Parse(GetOption(args, name, fallback.ToString("R", CultureInfo.InvariantCulture)),
            CultureInfo.InvariantCulture);
}
