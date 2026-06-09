using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DupFinder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isScanning;
        public ObservableCollection<DuplicateResult> Results { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void BrowseFolderAButton_Click(object sender, RoutedEventArgs e)
        {
            PromptFolderSelection(FolderAPathTextBox);
        }

        private void BrowseFolderBButton_Click(object sender, RoutedEventArgs e)
        {
            PromptFolderSelection(FolderBPathTextBox);
        }

        private void SwapButton_Click(object? sender, RoutedEventArgs e)
        {
            var temp = FolderAPathTextBox.Text;
            FolderAPathTextBox.Text = FolderBPathTextBox.Text;
            FolderBPathTextBox.Text = temp;
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning)
            {
                return;
            }

            var folderA = FolderAPathTextBox.Text.Trim();
            var folderB = FolderBPathTextBox.Text.Trim();
            var hasA = Directory.Exists(folderA);
            var hasB = Directory.Exists(folderB);

            if (!hasA && !hasB)
            {
                System.Windows.MessageBox.Show("존재하는 폴더를 하나 이상 선택해 주세요.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var singleFolderMode = hasA ^ hasB;
            var targetSingleFolder = hasA ? folderA : folderB;

            _isScanning = true;
            ToggleUiDuringScan(isScanning: true);
            StatusTag.Text = singleFolderMode ? "스캔 중... (단일 폴더)" : "스캔 중...";
            SummaryTextBlock.Text = singleFolderMode
                ? "단일 폴더 내에서 중복 파일을 찾고 있습니다."
                : "해시 계산 중입니다. 파일 수에 따라 시간이 걸릴 수 있습니다.";
            CurrentScanText.Text = "스캔 준비 중...";
            Results.Clear();

            try
            {
                var progress = new Progress<string>(UpdateCurrentScan);
                List<DuplicateResult> results;

                if (singleFolderMode)
                {
                    results = await Task.Run(() => ScanForDuplicatesWithinFolder(targetSingleFolder, progress));
                }
                else
                {
                    results = await Task.Run(() => ScanForDuplicates(folderA, folderB, progress));
                }

                Results.Clear();
                foreach (var item in results)
                {
                    Results.Add(item);
                }

                if (Results.Count == 0)
                {
                    StatusTag.Text = "중복 없음";
                    SummaryTextBlock.Text = singleFolderMode
                        ? "폴더 내에서 중복 파일을 찾지 못했습니다."
                        : "해시가 같은 파일을 찾지 못했습니다.";
                }
                else
                {
                    StatusTag.Text = $"중복 {Results.Count}건";
                    SummaryTextBlock.Text = singleFolderMode
                        ? "같은 해시의 파일 쌍을 찾았습니다. 유지할 파일을 선택하세요."
                        : "유지할 파일을 선택하세요.";
                }
            }
            catch (Exception ex)
            {
                StatusTag.Text = "오류 발생";
                SummaryTextBlock.Text = ex.Message;
            }
            finally
            {
                ToggleUiDuringScan(isScanning: false);
                CurrentScanText.Text = StatusTag.Text switch
                {
                    "오류 발생" => CurrentScanText.Text,
                    _ => "스캔 완료"
                };
                _isScanning = false;
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = System.Windows.MessageBox.Show(
                "선택한 폴더와 결과를 모두 초기화하시겠습니까?",
                "초기화 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            FolderAPathTextBox.Text = string.Empty;
            FolderBPathTextBox.Text = string.Empty;
            Results.Clear();
            StatusTag.Text = "대기 중";
            SummaryTextBlock.Text = "아직 스캔하지 않았습니다. 두 폴더를 선택한 뒤 스캔을 시작하세요.";
            CurrentScanText.Text = string.Empty;
        }

        private void KeepPathButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfButton button || button.CommandParameter is not DuplicateResult result || button.Tag is not string sideTag)
            {
                return;
            }

            result.SelectedKeep = sideTag.Equals("A", StringComparison.OrdinalIgnoreCase)
                ? result.PathA
                : result.PathB;

            StatusTag.Text = $"선택: {Path.GetFileName(result.SelectedKeep ?? string.Empty)}";
            SummaryTextBlock.Text = $"{(result.SelectedKeep ?? string.Empty)} 파일을 유지하도록 표시했습니다.";
        }

        private void OpenPathButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfButton button || button.CommandParameter is not string path || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                OpenInExplorer(path);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (Results.Count == 0)
            {
                System.Windows.MessageBox.Show("저장할 결과가 없습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "결과 저장 위치를 선택하세요",
                Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                FileName = $"DupFinder_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                AddExtension = true,
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    Results,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dialog.FileName, json);
                System.Windows.MessageBox.Show($"저장 완료: {dialog.FileName}", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PromptFolderSelection(WpfTextBox target)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "비교할 폴더를 선택하세요."
            };

            var result = dlg.ShowDialog();
            if (result == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                target.Text = dlg.SelectedPath;
            }
        }

        private void UpdateCurrentScan(string message)
        {
            CurrentScanText.Text = message;
        }

        private static bool IsBinaryFile(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0) // NULL 바이트가 있으면 바이너리 파일
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return true; // 에러 발생 시 바이너리로 취급하여 크기 비교 진행
            }
        }

        private static List<DuplicateResult> ScanForDuplicates(string folderA, string folderB, IProgress<string>? progress = null)
        {
            var duplicates = new List<DuplicateResult>();
            var textHashesA = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
            var binarySizeToPathsA = new Dictionary<long, List<string>>();

            // 1단계: 폴더 A 스캔
            foreach (var path in EnumerateFilesSafely(folderA))
            {
                if (IsBinaryFile(path))
                {
                    progress?.Report($"폴더 A 바이너리 스캔 (크기 수집): {path}");
                    var size = GetFileSize(path);
                    if (!binarySizeToPathsA.TryGetValue(size, out var list))
                    {
                        list = new List<string>();
                        binarySizeToPathsA[size] = list;
                    }
                    list.Add(path);
                }
                else
                {
                    progress?.Report($"폴더 A 텍스트 스캔 (즉시 해시 계산): {path}");
                    var hash = ComputeSha256(path);
                    if (hash is null)
                    {
                        continue;
                    }
                    var size = GetFileSize(path);
                    textHashesA[hash] = new FileEntry(path, size);
                }
            }

            // 폴더 A 바이너리 파일의 계산된 해시 캐싱용
            var computedBinaryHashesA = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 2단계: 폴더 B 스캔 및 비교
            foreach (var pathB in EnumerateFilesSafely(folderB))
            {
                if (IsBinaryFile(pathB))
                {
                    progress?.Report($"폴더 B 바이너리 스캔: {pathB}");
                    var size = GetFileSize(pathB);

                    if (binarySizeToPathsA.TryGetValue(size, out var pathsA))
                    {
                        string? hashB = null;

                        foreach (var pathA in pathsA)
                        {
                            if (!computedBinaryHashesA.TryGetValue(pathA, out var hashA))
                            {
                                progress?.Report($"바이너리 해시 계산 (A): {pathA}");
                                hashA = ComputeSha256(pathA);
                                if (hashA != null)
                                {
                                    computedBinaryHashesA[pathA] = hashA;
                                }
                            }

                            if (hashA == null)
                            {
                                continue;
                            }

                            if (hashB == null)
                            {
                                progress?.Report($"바이너리 해시 계산 (B): {pathB}");
                                hashB = ComputeSha256(pathB);
                            }

                            if (hashB == null)
                            {
                                break;
                            }

                            if (hashA.Equals(hashB, StringComparison.OrdinalIgnoreCase))
                            {
                                duplicates.Add(new DuplicateResult(
                                    hashB,
                                    pathA,
                                    pathB,
                                    size));
                            }
                        }
                    }
                }
                else
                {
                    progress?.Report($"폴더 B 텍스트 스캔 (즉시 해시 계산): {pathB}");
                    var hashB = ComputeSha256(pathB);
                    if (hashB is null)
                    {
                        continue;
                    }

                    if (textHashesA.TryGetValue(hashB, out var entryA))
                    {
                        var size = GetFileSize(pathB);
                        duplicates.Add(new DuplicateResult(
                            hashB,
                            entryA.Path,
                            pathB,
                            Math.Max(entryA.Size, size)));
                    }
                }
            }

            return duplicates.OrderBy(d => d.Hash).ToList();
        }

        private static List<DuplicateResult> ScanForDuplicatesWithinFolder(string folder, IProgress<string>? progress = null)
        {
            var hashGroups = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
            var sizeToPathsBinary = new Dictionary<long, List<string>>();

            // 1단계: 전체 파일을 순회하며 텍스트는 즉시 해시 계산, 바이너리는 크기별로 모음
            foreach (var path in EnumerateFilesSafely(folder))
            {
                if (IsBinaryFile(path))
                {
                    progress?.Report($"폴더 스캔 (바이너리 크기 수집): {path}");
                    var size = GetFileSize(path);
                    if (!sizeToPathsBinary.TryGetValue(size, out var list))
                    {
                        list = new List<string>();
                        sizeToPathsBinary[size] = list;
                    }
                    list.Add(path);
                }
                else
                {
                    progress?.Report($"텍스트 파일 스캔 (즉시 해시 계산): {path}");
                    var hash = ComputeSha256(path);
                    if (hash is null)
                    {
                        continue;
                    }
                    var size = GetFileSize(path);
                    if (!hashGroups.TryGetValue(hash, out var list))
                    {
                        list = new List<FileEntry>();
                        hashGroups[hash] = list;
                    }
                    list.Add(new FileEntry(path, size));
                }
            }

            // 2단계: 동일 크기의 바이너리 파일이 2개 이상 존재하는 경우에만 해시 계산 후 hashGroups에 병합
            foreach (var kvp in sizeToPathsBinary)
            {
                var size = kvp.Key;
                var paths = kvp.Value;
                if (paths.Count <= 1)
                {
                    continue;
                }

                foreach (var path in paths)
                {
                    progress?.Report($"바이너리 해시 계산: {path}");
                    var hash = ComputeSha256(path);
                    if (hash is null)
                    {
                        continue;
                    }

                    if (!hashGroups.TryGetValue(hash, out var list))
                    {
                        list = new List<FileEntry>();
                        hashGroups[hash] = list;
                    }
                    list.Add(new FileEntry(path, size));
                }
            }

            // 3단계: 중복 결과 리스트 구성
            var results = new List<DuplicateResult>();
            foreach (var kvp in hashGroups)
            {
                var entries = kvp.Value;
                if (entries.Count <= 1)
                {
                    continue;
                }

                var anchor = entries[0];
                for (int i = 1; i < entries.Count; i++)
                {
                    var dup = entries[i];
                    results.Add(new DuplicateResult(
                        kvp.Key,
                        anchor.Path,
                        dup.Path,
                        Math.Max(anchor.Size, dup.Size)));
                }
            }

            return results.OrderBy(r => r.Hash).ToList();
        }

        private static IEnumerable<string> EnumerateFilesSafely(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(current);
                }
                catch
                {
                    subDirs = Array.Empty<string>();
                }

                foreach (var dir in subDirs)
                {
                    pending.Push(dir);
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }

        private static string? ComputeSha256(string path)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(path);
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        private static long GetFileSize(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Length;
            }
            catch
            {
                return 0;
            }
        }

        private void ToggleUiDuringScan(bool isScanning)
        {
            ScanProgressBar.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
            ScanButton.IsEnabled = !isScanning;
            BrowseFolderAButton.IsEnabled = !isScanning;
            BrowseFolderBButton.IsEnabled = !isScanning;
            SwapButton.IsEnabled = !isScanning;
            ResultsGrid.IsEnabled = !isScanning;
        }

        private static void OpenInExplorer(string path)
        {
            string argument;
            if (Directory.Exists(path))
            {
                argument = $"\"{path}\"";
            }
            else if (File.Exists(path))
            {
                argument = $"/select,\"{path}\"";
            }
            else
            {
                throw new FileNotFoundException("경로를 찾을 수 없습니다.", path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = argument,
                UseShellExecute = true
            });
        }
    }

    public sealed class DuplicateResult : INotifyPropertyChanged
    {
        public DuplicateResult(string hash, string pathA, string pathB, long size)
        {
            Hash = hash;
            PathA = pathA;
            PathB = pathB;
            Size = size;
        }

        public string Hash { get; }
        public string PathA { get; }
        public string PathB { get; }
        public long Size { get; }

        private string? _selectedKeep;
        public string? SelectedKeep
        {
            get => _selectedKeep;
            set
            {
                if (_selectedKeep != value)
                {
                    _selectedKeep = value;
                    OnPropertyChanged(nameof(SelectedKeep));
                }
            }
        }

        public string SizeDisplay => FormatBytes(Size);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            var order = (int)Math.Floor(Math.Log(bytes, 1024));
            order = Math.Min(order, sizes.Length - 1);
            var adjusted = bytes / Math.Pow(1024, order);
            return $"{adjusted:0.##} {sizes[order]}";
        }
    }

    internal readonly record struct FileEntry(string Path, long Size);
}
