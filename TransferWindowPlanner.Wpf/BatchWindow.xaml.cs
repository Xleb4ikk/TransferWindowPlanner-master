using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Core = TrajectoryCalculator;

namespace TrajectoryCalculator.Gui;

public partial class BatchWindow : Window
{
    private readonly UiLanguage _language = UiTextCatalog.DetectInitialLanguage();
    private readonly ObservableCollection<Core.BatchItemResult> _results = [];

    private CancellationTokenSource? _cts;
    private Core.BatchRunResult? _lastResult;
    private Stopwatch? _runStopwatch;

    public BatchWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        MaxParallelBox.Text = Math.Max(1, Environment.ProcessorCount - 1).ToString();
        ApplyLanguage();
    }

    private string L(string en, string ru) => _language == UiLanguage.Russian ? ru : en;

    private void ApplyLanguage()
    {
        Title = L("Batch Calculation", "Пакетный расчёт");
        TitleText.Text = Title;
        LblSource.Text = L("Source", "Источник");
        LblInputPath.Text = L("Folder or manifest:", "Папка или манифест:");
        BtnBrowseFolder.Content = L("Folder…", "Папка…");
        BtnBrowseManifest.Content = L("Manifest…", "Манифест…");
        LblOutputDir.Text = L("Output folder:", "Папка результатов:");
        ChkRecursive.Content = L("Recursive", "Рекурсивно");
        LblMaxParallel.Text = L("Parallel:", "Потоков:");
        BtnRun.Content = L("▶ Run batch", "▶ Запустить пакет");
        BtnCancel.Content = L("Cancel", "Отмена");
        StatusText.Text = L("Ready.", "Готово к запуску.");
        BtnExportCsv.Content = L("Export CSV…", "Экспорт CSV…");
        BtnExportJson.Content = L("Export JSON…", "Экспорт JSON…");
        BtnOpenOutputFolder.Content = L("Open output folder", "Открыть папку результатов");
        BtnClose.Content = L("Close", "Закрыть");
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L("Select a folder of scenario JSON files", "Выберите папку со сценарными JSON-файлами")
        };

        if (dialog.ShowDialog(this) == true)
        {
            InputPathBox.Text = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(OutputDirBox.Text))
                OutputDirBox.Text = Path.Combine(dialog.FolderName, "batch-results");
        }
    }

    private void BrowseManifest_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("Select a batch manifest JSON", "Выберите манифест пакетного расчёта"),
            Filter = $"{L("JSON files", "JSON-файлы")}|*.json|{L("All files", "Все файлы")}|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            InputPathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(OutputDirBox.Text))
            {
                var directory = Path.GetDirectoryName(dialog.FileName) ?? Directory.GetCurrentDirectory();
                OutputDirBox.Text = Path.Combine(directory, "batch-results");
            }
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L("Select output folder", "Выберите папку для результатов")
        };

        if (dialog.ShowDialog(this) == true)
            OutputDirBox.Text = dialog.FolderName;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var inputPath = InputPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            MessageBox.Show(this, L("Choose a folder or manifest file first.", "Сначала выберите папку или файл манифеста."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isFolder = Directory.Exists(inputPath);
        if (!isFolder && !File.Exists(inputPath))
        {
            MessageBox.Show(this, L($"Path not found: {inputPath}", $"Путь не найден: {inputPath}"),
                Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDirBox.Text))
        {
            OutputDirBox.Text = isFolder
                ? Path.Combine(inputPath, "batch-results")
                : Path.Combine(Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory(), "batch-results");
        }

        var maxParallel = Math.Max(1, Environment.ProcessorCount - 1);
        if (int.TryParse(MaxParallelBox.Text, out var parsedParallel) && parsedParallel > 0)
            maxParallel = parsedParallel;

        var recursive = ChkRecursive.IsChecked == true;
        var outputDirectory = OutputDirBox.Text;

        _results.Clear();
        SummaryText.Text = string.Empty;
        RunProgress.Value = 0;
        RunProgress.Maximum = 1;
        BtnRun.IsEnabled = false;
        BtnCancel.IsEnabled = true;
        InputPathBox.IsEnabled = false;
        StatusText.Text = L("Running…", "Выполняется…");

        _cts = new CancellationTokenSource();
        _runStopwatch = Stopwatch.StartNew();

        var progress = new Progress<Core.BatchCalculator.Progress>(OnProgress);
        var options = new Core.BatchCalculator.Options
        {
            MaxDegreeOfParallelism = maxParallel,
            ProgressReporter = progress,
            CancellationToken = _cts.Token
        };

        try
        {
            var result = await Task.Run(
                () => isFolder
                    ? Core.BatchCalculator.RunFolder(inputPath, recursive, options)
                    : Core.BatchCalculator.RunManifestFile(inputPath, options),
                _cts.Token);

            _lastResult = result;
            Directory.CreateDirectory(outputDirectory);
            Core.BatchCalculator.WriteSummaryCsv(result, Path.Combine(outputDirectory, "batch-summary.csv"));
            Core.BatchCalculator.WriteSummaryJson(result, Path.Combine(outputDirectory, "batch-summary.json"));

            StatusText.Text = L("Done.", "Готово.");
            SummaryText.Text = L(
                $"Total: {result.TotalCount}   OK: {result.SuccessCount}   Failed: {result.FailureCount}   Elapsed: {result.TotalElapsedMs / 1000.0:0.0} s   Saved to: {outputDirectory}",
                $"Всего: {result.TotalCount}   Успешно: {result.SuccessCount}   Ошибок: {result.FailureCount}   Время: {result.TotalElapsedMs / 1000.0:0.0} с   Сохранено в: {outputDirectory}");
        }
        catch (OperationCanceledException)
        {
            var partialItems = _results.OrderBy(item => item.Index).ToList();
            _lastResult = new Core.BatchRunResult
            {
                Items = partialItems,
                SuccessCount = partialItems.Count(item => item.Success),
                FailureCount = partialItems.Count(item => !item.Success),
                TotalElapsedMs = _runStopwatch?.Elapsed.TotalMilliseconds ?? 0
            };
            StatusText.Text = L($"Cancelled after {partialItems.Count} items.", $"Отменено после {partialItems.Count} сценариев.");
        }
        catch (Exception ex)
        {
            StatusText.Text = L("Failed: ", "Ошибка: ") + ex.Message;
            MessageBox.Show(this, ex.Message, L("Batch error", "Ошибка пакетного расчёта"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRun.IsEnabled = true;
            BtnCancel.IsEnabled = false;
            InputPathBox.IsEnabled = true;
            _cts?.Dispose();
            _cts = null;
            _runStopwatch = null;
        }
    }

    private void OnProgress(Core.BatchCalculator.Progress p)
    {
        RunProgress.Maximum = p.Total;
        RunProgress.Value = p.Completed;
        StatusText.Text = L($"{p.Completed} / {p.Total} completed", $"{p.Completed} / {p.Total} обработано");
        _results.Add(p.Last);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        BtnCancel.IsEnabled = false;
        StatusText.Text = L("Cancelling…", "Отмена…");
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null)
        {
            MessageBox.Show(this, L("Run a batch first.", "Сначала запустите пакетный расчёт."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = L("Export batch summary as CSV", "Экспорт сводки в CSV"),
            Filter = "CSV|*.csv",
            FileName = "batch-summary.csv"
        };

        if (dialog.ShowDialog(this) == true)
            Core.BatchCalculator.WriteSummaryCsv(_lastResult, dialog.FileName);
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null)
        {
            MessageBox.Show(this, L("Run a batch first.", "Сначала запустите пакетный расчёт."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = L("Export batch summary as JSON", "Экспорт сводки в JSON"),
            Filter = "JSON|*.json",
            FileName = "batch-summary.json"
        };

        if (dialog.ShowDialog(this) == true)
            Core.BatchCalculator.WriteSummaryJson(_lastResult, dialog.FileName, Core.ScenarioJson.Options);
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = OutputDirBox.Text;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            MessageBox.Show(this, L("Output folder doesn't exist yet.", "Папка результатов ещё не существует."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
