using System.Text;

namespace DistributedRank;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                PrintUsage();
                return 0;
            }

            string[] options = args.Skip(1).ToArray();
            return args[0].ToLowerInvariant() switch
            {
                "master" => await MasterNode.RunAsync(options),
                "worker" => await WorkerNode.RunAsync(options),
                _ => throw new ArgumentException($"Unknown mode '{args[0]}'. Use 'master' or 'worker'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Distributed matrix rank (variant 1)");
        Console.WriteLine("  master --matrix data/matrix.txt --workers 3 --port 5000 [--tolerance 1e-10]");
        Console.WriteLine("         [--repeat 5] [--result results/latest.json]");
        Console.WriteLine("  worker --host 127.0.0.1 --port 5000 --id worker-1");
    }
}
