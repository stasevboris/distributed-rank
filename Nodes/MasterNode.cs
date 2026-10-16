using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DistributedRank;

/// <summary>
/// Coordinator node: loads the matrix, accepts worker TCP connections,
/// distributes rows round-robin, orchestrates distributed Gaussian elimination,
/// and compares the result with a local computation.
/// </summary>
internal static class MasterNode
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string matrixPath = CommandLine.GetOption(args, "--matrix", Path.Combine("data", "matrix.txt"));
        int workerCount = CommandLine.GetIntOption(args, "--workers", 3);
        int port = CommandLine.GetIntOption(args, "--port", 5000);
        int repeat = CommandLine.GetIntOption(args, "--repeat", 5);
        double relativeTolerance = CommandLine.GetDoubleOption(args, "--tolerance", 1e-10);
        if (workerCount < 1) throw new ArgumentOutOfRangeException("--workers", "Worker count must be at least one.");
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException("--port", "Port must be between 1 and 65535.");
        if (repeat < 1) throw new ArgumentException("Repeat must be at least one.");
        if (!double.IsFinite(relativeTolerance) || relativeTolerance <= 0)
            throw new ArgumentException("Tolerance must be finite and positive.");

        double[][] matrix = MatrixLoader.Load(matrixPath);
        double maxAbs = matrix.SelectMany(row => row).Select(Math.Abs).DefaultIfEmpty(0).Max();
        double epsilon = relativeTolerance * Math.Max(1.0, maxAbs);
        if (!double.IsFinite(epsilon)) throw new ArgumentException("Tolerance is too large.");

        Console.WriteLine("Distributed matrix rank — variant 1");
        Console.WriteLine($"Input: {Path.GetFullPath(matrixPath)} ({matrix.Length} x {matrix.Length})");
        Console.WriteLine($"Relative tolerance: {relativeTolerance:G}; absolute threshold: {epsilon:G}");
        MatrixLoader.Print(matrix);
        Console.WriteLine($"Warm-up: 1; measured repetitions: {repeat}");

        // --- Local (single-machine) rank computation ---
        int localRank = RankCalculator.ComputeRank(matrix, epsilon); // Warm-up excluded from measurements.
        var localSamples = new List<double>();
        for (int run = 0; run < repeat; run++)
        {
            Stopwatch localTimer = Stopwatch.StartNew();
            localRank = RankCalculator.ComputeRank(matrix, epsilon);
            localTimer.Stop();
            localSamples.Add(localTimer.Elapsed.TotalMilliseconds);
        }
        double localMean = localSamples.Average();
        Console.WriteLine($"One computer: rank = {localRank}; mean = {localMean:F3} ms");

        // --- Accept TCP connections from workers ---
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Listening on 0.0.0.0:{port}; waiting for {workerCount} worker(s)...");
        var workers = new List<WorkerConnection>();
        try
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (workers.Count < workerCount)
            {
                TcpClient client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(90));
                client.NoDelay = true;
                var stream = client.GetStream();
                var reader = Protocol.CreateReader(stream);
                var writer = Protocol.CreateWriter(stream);
                var connection = new WorkerConnection(client, reader, writer);
                workers.Add(connection);
                WireMessage hello = await Protocol.ReadMessageAsync(reader).WaitAsync(TimeSpan.FromSeconds(30));
                if (hello.Type != "hello" || string.IsNullOrWhiteSpace(hello.WorkerId))
                    throw new IOException("A connected client did not send a valid worker hello message.");
                if (!ids.Add(hello.WorkerId)) throw new IOException($"Duplicate worker ID '{hello.WorkerId}'.");
                connection.Id = hello.WorkerId;
                connection.Platform = hello.Platform ?? "unknown";
                Console.WriteLine($"Connected: {connection.Id} ({workers.Count}/{workerCount})");
            }

            // --- Partition rows round-robin ---
            var partitions = Enumerable.Range(0, workerCount)
                .Select(_ => new List<RowMessage>())
                .ToArray();
            for (int row = 0; row < matrix.Length; row++)
                partitions[row % workerCount].Add(new RowMessage(row, matrix[row]));

            // --- Distributed Gaussian elimination (one warm-up + measured runs) ---
            var totalSamples = new List<double>();
            var networkSamples = new List<double>();
            int distributedRank = 0;
            for (int run = -1; run < repeat; run++)
            {
                Stopwatch totalTimer = Stopwatch.StartNew();
                for (int i = 0; i < workers.Count; i++)
                    await Protocol.SendMessageAsync(workers[i].Writer, new WireMessage { Type = "init", Rows = partitions[i] });
                foreach (WorkerConnection worker in workers)
                {
                    WireMessage ready = await Protocol.ReadMessageAsync(worker.Reader).WaitAsync(TimeSpan.FromSeconds(30));
                    if (ready.Type != "ready") throw new IOException($"Worker {worker.Id} failed to initialize.");
                }

                distributedRank = 0;
                Stopwatch distributedTimer = Stopwatch.StartNew();
                for (int column = 0; column < matrix.Length; column++)
                {
                    // Ask each worker for its best pivot candidate in this column.
                    await Task.WhenAll(workers.Select(worker =>
                        Protocol.SendMessageAsync(worker.Writer, new WireMessage { Type = "findPivot", Column = column })));

                    WireMessage[] candidates = await Task.WhenAll(workers.Select(worker => Protocol.ReadMessageAsync(worker.Reader)));
                    WireMessage? best = candidates
                        .Where(candidate => candidate.Type == "candidate" && candidate.RowId >= 0 && candidate.Values is not null)
                        .OrderByDescending(candidate => Math.Abs(candidate.Value))
                        .ThenBy(candidate => candidate.RowId)
                        .FirstOrDefault();

                    if (best is null || Math.Abs(best.Value) <= epsilon)
                    {
                        continue; // Later columns may still have nonzero pivots.
                    }

                    // Broadcast the pivot row for elimination.
                    await Task.WhenAll(workers.Select(worker =>
                        Protocol.SendMessageAsync(worker.Writer, new WireMessage
                        {
                            Type = "eliminate",
                            Column = column,
                            RowId = best.RowId,
                            Values = best.Values
                        })));
                    WireMessage[] acknowledgements = await Task.WhenAll(workers.Select(worker => Protocol.ReadMessageAsync(worker.Reader)));
                    if (acknowledgements.Any(message => message.Type != "ack"))
                        throw new IOException("A worker did not acknowledge the elimination step.");

                    distributedRank++;
                }
                distributedTimer.Stop();
                totalTimer.Stop();
                if (localRank != distributedRank)
                    throw new InvalidOperationException("Sequential and distributed results differ.");
                if (run >= 0)
                {
                    totalSamples.Add(totalTimer.Elapsed.TotalMilliseconds);
                    networkSamples.Add(distributedTimer.Elapsed.TotalMilliseconds);
                }
            }

            // --- Report results ---
            await Task.WhenAll(workers.Select(worker => Protocol.SendMessageAsync(worker.Writer, new WireMessage { Type = "stop" })));
            double totalMean = totalSamples.Average();
            Console.WriteLine($"Distributed ({workerCount} worker nodes): rank = {distributedRank}; mean = {totalMean:F3} ms (includes row transfer)");
            Console.WriteLine($"Network calculation phase: mean = {networkSamples.Average():F3} ms");
            Console.WriteLine($"Ranks agree: {localRank == distributedRank}");
            if (localRank != distributedRank) throw new InvalidOperationException("Sequential and distributed results differ.");
            Console.WriteLine($"Speedup (single/distributed): {localMean / totalMean:F4}x");
            string resultPath = Path.GetFullPath(CommandLine.GetOption(args, "--result", "results/latest.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            var result = new
            {
                measuredAt = DateTimeOffset.UtcNow,
                matrix = Path.GetFileName(matrixPath),
                size = matrix.Length,
                rank = localRank,
                masterPlatform = RuntimeInformation.OSDescription,
                runtime = RuntimeInformation.FrameworkDescription,
                workers = workers.Select(w => new { id = w.Id, platform = w.Platform }).ToArray(),
                repeat,
                epsilon,
                localSamples,
                distributedSamples = totalSamples,
                networkSamples,
                localMeanMs = localMean,
                distributedMeanMs = totalMean,
                speedup = localMean / totalMean,
                timing = "One warm-up excluded; connection setup excluded; row transfer included in total."
            };
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result,
                new JsonSerializerOptions(Protocol.JsonOptions) { WriteIndented = true }));
            Console.WriteLine($"Saved: {resultPath}");
            return 0;
        }
        finally
        {
            listener.Stop();
            foreach (WorkerConnection worker in workers) worker.Dispose();
        }
    }
}
