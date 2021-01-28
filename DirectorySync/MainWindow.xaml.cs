using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using DirectorySync.Models;
using FileCompare;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;

namespace DirectorySync
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private FileFinder Finder { get; set; }

        private string _ingoresFilePath = "";

        private string IgnoreFilePath
        {
            get => _ingoresFilePath;
            set
            {
                _ingoresFilePath = value;
                ChangeIgnoreFilePathButton.Text = value.Split("\\")[^1].Replace(".ignores", "");
            }
        }

        private string _path1 = "";
        private string _path2 = "";

        private DirectoryInfo LeftFolder
        {
            get => new(_path1);
            set
            {
                _path1 = value.FullName;
                LeftPathTextBox.Text = value.FullName;
            }
        }

        private DirectoryInfo RightFolder
        {
            get => new(_path2);
            set
            {
                _path2 = value.FullName;
                RightPathTextBox.Text = value.FullName;
            }
        }

        private static string AppDataString =>
            $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\DirectorySync";
        private static string LastRunPath => $@"{AppDataString}\.lastrun";
        private static string IgnoresFolder => $@"{AppDataString}\ignores\";

        private ObservableCollection<ComparisonResult> ComparisonResults { get; } = new();

        public MainWindow()
        {
            InitializeComponent();

            ComparisonResults.CollectionChanged += ComparisonResults_CollectionChanged;
            Results.ItemsSource = ComparisonResults;

            if (File.Exists(LastRunPath)) LoadSettings();
            else FirstTimeSetup();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SaveSettings();
        }

        private void LoadSettings()
        {
            var settingsFileText = File.ReadAllText(LastRunPath);
            var settings = JsonSerializer.Deserialize<PreviousRunSettings>(settingsFileText);
            if (settings == null) return;
            LeftFolder = new DirectoryInfo(settings.Folder1Path);
            RightFolder = new DirectoryInfo(settings.Folder2Path);
            IgnoreFilePath = settings.IgnorePath;
            Finder = new FileFinder(IgnoreFilePath);
        }

        private void FirstTimeSetup()
        {
            if (!Directory.Exists(IgnoresFolder))
            {
                Directory.CreateDirectory(IgnoresFolder);
            }

            var defaultIgnoresPath = IgnoresFolder + "default.ignores";
            if (!File.Exists(defaultIgnoresPath))
            {
                File.WriteAllText(defaultIgnoresPath, "");
                IgnoreFilePath = defaultIgnoresPath;
            }

            Folder1Path_MouseDown(null, null);
            if (LeftFolder.FullName == "")
                Environment.Exit(0);

            Folder2Path_MouseDown(null, null);
            if (RightFolder.FullName == "")
                Environment.Exit(0);

            SaveSettings();
        }

        private void SaveSettings()
        {
            Finder = new FileFinder(IgnoreFilePath);
            SaveChanges.IsEnabled = false;
            ClearChanges.IsEnabled = false;
            var settingsAsJson = JsonSerializer.Serialize(new PreviousRunSettings
            {
                Folder1Path = LeftFolder.FullName,
                Folder2Path = RightFolder.FullName,
                IgnorePath = IgnoreFilePath
            });
            File.WriteAllText(LastRunPath, settingsAsJson);
        }

        private void ComparisonResults_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var identicalFiles = 0;
            var targetsMissingLeft = 0;
            var targetsMissingRight = 0;
            var newerOriginals = 0;
            var newerTargets = 0;

            for (int i = 0; i < ComparisonResults.Count; i++)
            {
                switch (ComparisonResults[i].Status)
                {
                    case MatchStatus.FilesAreTheSame:
                        identicalFiles++;
                        break;
                    case MatchStatus.MissingFromLeft:
                        targetsMissingLeft++;
                        break;
                    case MatchStatus.MissingFromRight:
                        targetsMissingRight++;
                        break;
                    case MatchStatus.OriginalIsNewer:
                        newerOriginals++;
                        break;
                    case MatchStatus.TargetIsNewer:
                        newerTargets++;
                        break;
                }
            }

            ShowIdentical.Content = $"Identical ({identicalFiles})";
            ShowLeftMissing.Content = $"Missing ({targetsMissingLeft})";
            ShowRightMissing.Content = $"Missing ({targetsMissingRight})";
            ShowOriginalNewer.Content = $"Original Newer ({newerOriginals})";
            ShowTargetNewer.Content = $"Target Newer ({newerTargets})";
            Total.Content = $"{ComparisonResults.Count} file(s)";
        }

        private async void RunCompare_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(LeftFolder.FullName) || string.IsNullOrEmpty(RightFolder.FullName))
                return;

            ComparisonResults.Clear();
            LoadProgress.Maximum = 0;
            LoadProgress.Value = 0;

            try
            {
                await GetFiles();
            }
            catch (System.Text.RegularExpressions.RegexParseException ex)
            {
                MessageBox.Show(
                    $"There was an error parsing the ignore file, please fix and try again.\r\r{ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

            ShowFilterChanged(null, null);
            LoadProgress.Value = 0;
        }

        private async Task GetFiles()
        {
            if (!LeftFolder.Exists || !RightFolder.Exists) return;

            async Task<Dictionary<string, FileInfo>> GetFiles(DirectoryInfo folder)
            {
                var results = new Dictionary<string, FileInfo>();
                var fileNameSplitIndex = folder.FullName.Length + 1;

                await foreach (var file in Finder.SearchDirectoryAsync(fileNameSplitIndex, folder))
                {
                    var key = file.FullName[fileNameSplitIndex..];
                    results.Add(key, file);
                }

                return results;
            }

            var taskResults = await Task.WhenAll(new[] {
                GetFiles(LeftFolder),
                GetFiles(RightFolder)
            });

            var files = new Dictionary<string, FileGroup>();
            for (int i = 0; i < taskResults.Length; i++)
            {
                foreach (var (key, file) in taskResults[i])
                {
                    if (files.ContainsKey(key))
                    {
                        files[key] = i == 0
                            ? new FileGroup(file, files[key].right)
                            : new FileGroup(files[key].left, file);
                    }
                    else
                    {
                        files.Add(key, i == 0
                            ? new FileGroup(file, null)
                            : new FileGroup(null, file));
                    }
                }
            }

            int b = 0;
            await foreach (var comparison in FindDifferences(files))
            {
                b++;

                Dispatcher.Invoke(() =>
                {
                    ComparisonResults.Add(comparison);
                    LoadProgress.Value++;
                });

                if (b == 512)
                {
                    DoEvents();
                    b = 0;
                }
            }
        }

        public void DoEvents()
        {
            static object? ExitFrame(object f)
            {
                ((DispatcherFrame)f).Continue = false;
                return null;
            }

            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        private static async IAsyncEnumerable<ComparisonResult> FindDifferences(IReadOnlyDictionary<string, FileGroup> files)
        {
            static MatchStatus GetStatus(FileInfo left, FileInfo right)
            {
                var timeDifference = left.LastWriteTime - right.LastWriteTime;

                if (timeDifference.Seconds > 0)
                    return MatchStatus.OriginalIsNewer;

                if (timeDifference.Seconds < 0)
                    return MatchStatus.TargetIsNewer;

                if (left.Length != right.Length)
                    return MatchStatus.FilesAreDifferent;

                return MatchStatus.FilesAreTheSame;
            }

            foreach (var (key, (left, right)) in files)
            {
                yield return (left, right) switch
                {
                    (_, null) => new ComparisonResult
                    {
                        Name = key,
                        LeftDate = left.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        LeftSize = left.Length,
                        Status = MatchStatus.MissingFromRight
                    },
                    (null, _) => new ComparisonResult
                    {
                        Name = key,
                        RightDate = right!.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        RightSize = right.Length,
                        Status = MatchStatus.MissingFromLeft
                    },
                    _ => new ComparisonResult
                    {
                        Name = key,
                        LeftDate = left!.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        LeftSize = left.Length,
                        RightDate = right!.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        RightSize = right.Length,
                        Status = GetStatus(left, right)
                    },
                };
            }
        }

        private void ShowFilterChanged(object sender, RoutedEventArgs e)
        {
            if (Results?.Items != null)
                Results.Items.Filter = IsResultVisible;
        }

        private bool IsResultVisible(object obj)
        {
            var result = (ComparisonResult)obj;

            if (SearchBox.Text != "" && !result.Name.ToLower().Contains(SearchBox.Text.ToLower()))
                return false;

            return result.Status switch
            {
                MatchStatus.NotProcessed => true,
                MatchStatus.FilesAreTheSame when (ShowIdentical.IsChecked ?? false) => true,
                MatchStatus.MissingFromLeft when (ShowLeftMissing.IsChecked ?? true) => true,
                MatchStatus.MissingFromRight when (ShowRightMissing.IsChecked ?? true) => true,
                MatchStatus.OriginalIsNewer when (ShowOriginalNewer.IsChecked ?? true) => true,
                MatchStatus.TargetIsNewer when (ShowTargetNewer.IsChecked ?? true) => true,
                _ => false
            };
        }

        #region File Operations

        private void CommitChanges_Click(object sender, RoutedEventArgs e)
        {
            foreach (var comparison in ComparisonResults)
            {
                switch (comparison.Resolution)
                {
                    case ResolutionAction.CopyLeft:
                        try
                        {
                            CopyFile(comparison, LeftFolder, RightFolder);
                            comparison.Status = MatchStatus.FilesAreTheSame;
                            comparison.Resolution = ResolutionAction.Nothing;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("There was an error copying the file\r\n" + ex.Message);
                        }
                        break;

                    case ResolutionAction.CopyRight:
                        try
                        {
                            CopyFile(comparison, RightFolder, LeftFolder);
                            comparison.Status = MatchStatus.FilesAreTheSame;
                            comparison.Resolution = ResolutionAction.Nothing;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("There was an error copying the file\r\n" + ex.Message);
                        }
                        break;

                    case ResolutionAction.Delete:
                        try
                        {
                            var leftPath = LeftFolder.FullName + "/" + comparison.Name;
                            if (File.Exists(leftPath)) File.Delete(leftPath);

                            var rightPath = RightFolder.FullName + "/" + comparison.Name;
                            if (File.Exists(rightPath)) File.Delete(rightPath);
                        
                            ComparisonResults.Remove(comparison);
                        }
                        catch (Exception ex)
                        {
                           MessageBox.Show("There was an error deleting the file\r\n" + ex.Message);
                        }
                        break;
                }
            }
        }

        private static void CopyFile(ComparisonResult file, DirectoryInfo from, DirectoryInfo to)
        {
            var originalPath = from.FullName + "/" + file.Name;
            var targetPathInfo = new FileInfo(to.FullName + "/" + file.Name);

            if (targetPathInfo.Directory != null && !targetPathInfo.Directory.Exists)
                targetPathInfo.Directory.Create();

            File.Copy(originalPath, targetPathInfo.FullName, true);
        }

        private void IgnoreFileName(object sender, RoutedEventArgs e)
        {
            var files = (from ComparisonResult item in Results.SelectedItems select item).ToList();
            if (!files.Any()) return;

            foreach (var file in files)
            {
                Finder.IgnoreFileName(file.Name.Split("\\")[^1]);
                ComparisonResults.Remove(file);
            }

            SaveChanges.IsEnabled = true;
            ClearChanges.IsEnabled = true;
        }

        private void IgnoreFileFullPath(object sender, RoutedEventArgs e)
        {
            var files = (from ComparisonResult item in Results.SelectedItems select item).ToList();
            if (!files.Any()) return;

            foreach (var file in files)
            {
                Finder.IgnoreFilePath(file.Name);
                ComparisonResults.Remove(file);
            }

            SaveChanges.IsEnabled = true;
            ClearChanges.IsEnabled = true;
        }

        private void IgnoreFolder(object sender, RoutedEventArgs e)
        {
            var files = (from ComparisonResult item in Results.SelectedItems select item).ToList();
            if (!files.Any()) return;

            foreach (var file in files)
            {
                string folderPath = string.Join("\\", file.Name.Split("\\")[..^1]);
                Finder.IgnoreFolderPath(folderPath);

                ComparisonResults.Remove(file);
                foreach (var res in ComparisonResults.Where(r => r.Name.StartsWith(folderPath)).ToArray())
                    ComparisonResults.Remove(res);
            }

            SaveChanges.IsEnabled = true;
            ClearChanges.IsEnabled = true;
        }

        private void CompareFiles(object sender, RoutedEventArgs e)
        {
            if (!(Results.SelectedItem is ComparisonResult comparison)) return;
            if (string.IsNullOrEmpty(comparison.Name)) return;

            var text1 = File.ReadAllText(LeftFolder.FullName + "\\" + comparison.Name);
            var text2 = File.ReadAllText(RightFolder.FullName + "\\" + comparison.Name);
            var textCompare = new TextCompare(text1, text2);
            textCompare.Show();
        }

        #endregion

        #region UI Methods

        private static void SetActions(System.Collections.IList selectedItems, ResolutionAction resolution)
        {
            foreach (ComparisonResult result in selectedItems)
                result.Resolution = resolution;
        }

        private void SetDoNothing(object sender, RoutedEventArgs e) =>
            SetActions(Results.SelectedItems, ResolutionAction.Nothing);

        private void SetCopyFileRight(object sender, RoutedEventArgs e) =>
            SetActions(Results.SelectedItems, ResolutionAction.CopyRight);

        private void SetCopyFileLeft(object sender, RoutedEventArgs e) =>
            SetActions(Results.SelectedItems, ResolutionAction.CopyLeft);

        private void SetDeleteFile(object sender, RoutedEventArgs e) =>
            SetActions(Results.SelectedItems, ResolutionAction.Delete);

        private void ChangeIgnoreFilePathButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose Ignore File",
                Filter = "Ignore File (*.ignores)|*.ignores",
                InitialDirectory = IgnoresFolder
            };
            if (!(openFileDialog.ShowDialog() ?? false)) return;
            IgnoreFilePath = openFileDialog.FileName;
            SaveSettings();
        }

        private void Folder1Path_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var result = LegacyFolderPicker.GetFolder("Select original folder");
            if (result != "") LeftFolder = new DirectoryInfo(result);
            SaveSettings();
        }

        private void Folder2Path_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var result = LegacyFolderPicker.GetFolder("Select comparison folder");
            if (result != "") RightFolder = new DirectoryInfo(result);
            SaveSettings();
        }

        private void HandleSearchBox_KeyUp(object sender, KeyEventArgs e) => ShowFilterChanged(null, null);

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var result = Results.SelectedItem as ComparisonResult;
                if (result != null) Process.Start(LeftFolder.FullName + "\\" + result.Name);
            }
            catch
            {
                // ignored
            }
        }

        private void TestPath_OnClick(object sender, RoutedEventArgs e)
        {
            var window = new TestPathWindow(IgnoreFilePath, LeftFolder.FullName);
            window.ShowDialog();
        }

        private void ClearChanges_OnClick(object sender, RoutedEventArgs e)
        {
            Finder.ResetFilters();
            SaveChanges.IsEnabled = false;
            ClearChanges.IsEnabled = false;
        }

        private void SaveChanges_OnClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(Finder.SaveFilters());
            SaveChanges.IsEnabled = false;
            ClearChanges.IsEnabled = false;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void SwapPaths_Click(object sender, RoutedEventArgs e)
        {
            var leftCurrent = LeftFolder.FullName;
            var rightCurrent = RightFolder.FullName;
            LeftFolder = new DirectoryInfo(rightCurrent);
            RightFolder = new DirectoryInfo(leftCurrent);

            for (int i = 0; i < ComparisonResults.Count; i++)
            {
                var currentLeftDate = ComparisonResults[i].LeftDate;
                var currentLeftSize = ComparisonResults[i].LeftSize;

                ComparisonResults[i].Name = ComparisonResults[i].Name;
                ComparisonResults[i].LeftDate = ComparisonResults[i].RightDate;
                ComparisonResults[i].LeftSize = ComparisonResults[i].RightSize;
                ComparisonResults[i].RightDate = currentLeftDate;
                ComparisonResults[i].RightSize = currentLeftSize;
                ComparisonResults[i].Status = ComparisonResults[i].Status switch
                {
                    MatchStatus.MissingFromLeft => MatchStatus.MissingFromRight,
                    MatchStatus.MissingFromRight => MatchStatus.MissingFromLeft,
                    MatchStatus.OriginalIsNewer => MatchStatus.TargetIsNewer,
                    MatchStatus.TargetIsNewer => MatchStatus.OriginalIsNewer,
                    _ => ComparisonResults[i].Status
                };
                ComparisonResults[i].Resolution = ComparisonResults[i].Resolution switch
                {
                    ResolutionAction.CopyLeft => ResolutionAction.CopyRight,
                    ResolutionAction.CopyRight => ResolutionAction.CopyLeft,
                    _ => ComparisonResults[i].Resolution
                };
            }
            Results.ItemsSource = null;
            Results.ItemsSource = ComparisonResults;
            ComparisonResults_CollectionChanged(null, null);
            SaveSettings();
        }

        #endregion
    }

    internal struct FileGroup
    {
        public FileInfo? left { get; }
        public FileInfo? right { get; }

        public FileGroup(FileInfo? left, FileInfo? right)
        {
            this.left = left;
            this.right = right;
        }

        public override bool Equals(object obj)
        {
            return obj is FileGroup other &&
                   EqualityComparer<FileInfo>.Default.Equals(left, other.left) &&
                   EqualityComparer<FileInfo>.Default.Equals(right, other.right);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(left, right);
        }

        public void Deconstruct(out FileInfo left, out FileInfo right)
        {
            left = this.left;
            right = this.right;
        }

        public static implicit operator (FileInfo left, FileInfo right)(FileGroup value)
        {
            return (value.left, value.right);
        }

        public static implicit operator FileGroup((FileInfo left, FileInfo right) value)
        {
            return new FileGroup(value.left, value.right);
        }
    }
}