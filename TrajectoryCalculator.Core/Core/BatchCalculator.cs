using System.Text.Json;

namespace TrajectoryCalculator;

/// <summary>
/// Result of a single batch processing item.
/// </summary>
public sealed class BatchItemResult
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string DepartureDate { get; set; } = string.Empty;
    public string TravelDuration { get; set; } = string.Empty;
    public double DVTransferTotal { get; set; }
    public double MissionTotalDeltaV { get; set; }
    public double ElapsedMs { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Aggregate result of a batch processing run.
/// </summary>
public sealed class BatchRunResult
{
    public List<BatchItemResult> Items { get; set; } = [];
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int TotalCount => Items.Count;
    public double TotalElapsedMs { get; set; }
}

/// <summary>
/// Low-level batch trajectory calculator.
/// </summary>
public static class BatchCalculator
{
    public sealed class Progress
    {
        public int Total { get; set; }
        public int Completed { get; set; }
        public BatchItemResult Last { get; set; } = new();
    }

    public sealed class Options
    {
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount - 1;
        public IProgress<Progress>? ProgressReporter { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }

    public static BatchRunResult RunFolder(string folderPath, bool recursive, Options options)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(folderPath, "*.json", searchOption);
        return RunFiles([.. files], options);
    }

    public static BatchRunResult RunManifestFile(string manifestPath, Options options)
    {
        var json = File.ReadAllText(manifestPath);
        var entries = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var files = entries.Select(e => Path.IsPathRooted(e) ? e : Path.GetFullPath(Path.Combine(baseDir, e))).ToList();
        return RunFiles(files, options);
    }

    public static void WriteSummaryCsv(BatchRunResult result, string path)
    {
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Index,Id,Status,Mode,Origin,Destination,DepartureDate,TravelTime,DVTransfer,DVMission,ElapsedMs,Error");
        foreach (var item in result.Items)
        {
            writer.WriteLine($"{item.Index},{EscapeCsv(item.Id)},{item.StatusText},{EscapeCsv(item.Mode)},{EscapeCsv(item.Origin)},{EscapeCsv(item.Destination)},{EscapeCsv(item.DepartureDate)},{EscapeCsv(item.TravelDuration)},{item.DVTransferTotal:F1},{item.MissionTotalDeltaV:F1},{item.ElapsedMs:F0},{EscapeCsv(item.ErrorMessage)}");
        }
    }

    public static void WriteSummaryJson(BatchRunResult result, string path, JsonSerializerOptions? jsonOptions = null)
    {
        jsonOptions ??= ScenarioJson.Options;
        var json = JsonSerializer.Serialize(result, jsonOptions);
        File.WriteAllText(path, json);
    }

    private static BatchRunResult RunFiles(List<string> files, Options options)
    {
        var results = new List<BatchItemResult>();
        var total = files.Count;
        var completed = 0;
        var lockObj = new object();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Parallel.ForEach(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = options.CancellationToken
        }, (file, state, index) =>
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            var itemResult = new BatchItemResult { Index = (int)index, Id = Path.GetFileNameWithoutExtension(file) };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var json = File.ReadAllText(file);
                var input = JsonSerializer.Deserialize<ScenarioInput>(json, ScenarioJson.Options);
                if (input is null) throw new InvalidOperationException("Failed to deserialize scenario.");
                var execResult = ScenarioExecutor.Execute(input, Path.GetDirectoryName(file), ScenarioJson.Options);
                itemResult.Success = true;
                itemResult.StatusText = "OK";
                itemResult.Mode = execResult.Mode;
                itemResult.Origin = input.Request?.Origin ?? "";
                itemResult.Destination = input.Request?.Destination ?? "";
                itemResult.DVTransferTotal = execResult.Transfer?.DVTotal ?? 0;
                itemResult.MissionTotalDeltaV = (execResult.Transfer?.DVTotal ?? 0) + (execResult.Launch?.RequiredDeltaVMps ?? 0);
                if (execResult.Calendar is not null && execResult.Transfer is not null)
                {
                    itemResult.DepartureDate = execResult.Calendar.FormatDate(execResult.Transfer.DepartureTime);
                    itemResult.TravelDuration = execResult.Calendar.FormatDuration(execResult.Transfer.TravelTime);
                }
            }
            catch (Exception ex)
            {
                itemResult.Success = false;
                itemResult.StatusText = "FAIL";
                itemResult.ErrorMessage = ex.Message;
            }

            sw.Stop();
            itemResult.ElapsedMs = sw.Elapsed.TotalMilliseconds;

            lock (lockObj)
            {
                results.Add(itemResult);
                completed++;
                options.ProgressReporter?.Report(new Progress
                {
                    Total = total,
                    Completed = completed,
                    Last = itemResult
                });
            }
        });

        stopwatch.Stop();
        return new BatchRunResult
        {
            Items = [.. results.OrderBy(r => r.Index)],
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success),
            TotalElapsedMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
