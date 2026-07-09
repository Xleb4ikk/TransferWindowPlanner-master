using System.Diagnostics;
using System.Text.Json;

namespace StandaloneTrajectoryCalculator.Gui;

public class RocketBuilderService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public string DefaultModelDirectory { get; }
    private readonly string _repoRoot;

    public RocketBuilderService()
    {
        _repoRoot = ResolveRepoRoot();
        DefaultModelDirectory = FindDefaultModel(_repoRoot);
    }

    public List<RocketBuilderModelOption> GetAvailableModels()
    {
        var results = new List<RocketBuilderModelOption>();
        var artifactsDir = Path.Combine(_repoRoot, "rocket_builder_ml", "artifacts");
        if (!Directory.Exists(artifactsDir)) return results;

        foreach (var sub in Directory.GetDirectories(artifactsDir))
        {
            var modelPt = Path.Combine(sub, "model.pt");
            var metaJson = Path.Combine(sub, "metadata.json");
            if (File.Exists(modelPt) && File.Exists(metaJson))
                results.Add(new RocketBuilderModelOption
                {
                    Name = Path.GetFileName(sub),
                    DirectoryPath = sub
                });
        }
        return results;
    }

    public async Task<RocketBuilderPredictionExecution> PredictAsync(
        RocketBuilderMissionRequest request, string modelDir,
        string pythonExe, string device, int topK)
    {
        var requestJson = JsonSerializer.Serialize(request, JsonOpts);
        var reqFile = Path.Combine(Path.GetTempPath(), $"rb_req_{Guid.NewGuid():N}.json");
        var resFile = Path.Combine(Path.GetTempPath(), $"rb_res_{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(reqFile, requestJson);

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"-m rocket_builder_ml.predict --model-dir \"{modelDir}\" --input-json \"{reqFile}\" --output-json \"{resFile}\" --device {device} --top-k {topK}",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!File.Exists(resFile))
            {
                var msg = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                throw new InvalidOperationException(
                    $"Prediction failed (exit code {proc.ExitCode}):\n{msg.Trim()}");
            }

            var responseJson = await File.ReadAllTextAsync(resFile);
            var payload = JsonSerializer.Deserialize<RocketBuilderPredictionPayload>(responseJson, JsonOpts)
                ?? throw new InvalidOperationException("Failed to parse prediction response.");

            return new RocketBuilderPredictionExecution
            {
                Payload = payload,
                RequestJson = requestJson,
                ResponseJson = responseJson,
                ModelDirectory = modelDir,
                WorkingDirectory = _repoRoot,
                StandardOutput = stdout,
                StandardError = stderr
            };
        }
        finally
        {
            TryDelete(reqFile);
            TryDelete(resFile);
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "TransferWindowPlanner.sln")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return dir;
    }

    private static string FindDefaultModel(string repoRoot)
    {
        var artifacts = Path.Combine(repoRoot, "rocket_builder_ml", "artifacts");
        if (!Directory.Exists(artifacts)) return artifacts;
        var dirs = Directory.GetDirectories(artifacts);
        return dirs.Length > 0 ? dirs[0] : artifacts;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
