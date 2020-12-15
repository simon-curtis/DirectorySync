using FileCompare;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace DirectorySync
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private string _ingoresFilePath = "";

        private DirectoryInfo _path1;
        private DirectoryInfo _path2;
        private DateTime _folder1CreationDate;
        private DateTime _folder2CreationDate;

        private string IgnoreFilePath
        {
            get => _ingoresFilePath;
            set
            {
                _ingoresFilePath = value;
                IgnoreFilePathButton.Text = value.Split("\\")[^1].Replace(".ignores", "");
            }
        }

        private DirectoryInfo Folder1Path
        {
            get => _path1;
            set
            {
                _path1 = value;
                Folder1PathTextBox.Text = value.FullName;
                _folder1CreationDate = value.CreationTime;
            }
        }

        private DirectoryInfo Folder2Path
        {
            get => _path2;
            set
            {
                _path2 = value;
                Folder2PathTextBox.Text = value.FullName;
                _folder2CreationDate = value.CreationTime;
            }
        }

        private static string AppDataString =>
            $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\DirectorySync";

        private static string LastRunPath => $@"{AppDataString}\.lastrun";
        private static string IgnoresFolder => $@"{AppDataString}\ignores\";

        private readonly ObservableCollection<ComparisonResult> _comparisonResults =
            new ObservableCollection<ComparisonResult>();

        public MainWindow()
        {
            InitializeComponent();

            _comparisonResults.CollectionChanged += ComparisonResults_CollectionChanged;
            Results.ItemsSource = _comparisonResults;

            if (File.Exists(LastRunPath)) LoadSettings();
            else FirstTimeSetup();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SaveSettings();
            base.OnClosing(e);
        }

        private void LoadSettings()
        {
            var settingsFileText = File.ReadAllText(LastRunPath);
            var settings = JsonSerializer.Deserialize<PreviousRunSettings>(settingsFileText);
            Folder1Path = new DirectoryInfo(settings.Folder1Path);
            Folder2Path = new DirectoryInfo(settings.Folder2Path);
            IgnoreFilePath = settings.IgnorePath;
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
            if (Folder1Path.FullName == "")
                Application.Current.Shutdown();

            Folder2Path_MouseDown(null, null);
            if (Folder2Path.FullName == "")
                Application.Current.Shutdown();

            SaveSettings();
        }

        private void SaveSettings()
        {
            var settingsAsJson = JsonSerializer.Serialize(new PreviousRunSettings
            {
                Folder1Path = Folder1Path.FullName,
                Folder2Path = Folder2Path.FullName,
                IgnorePath = IgnoreFilePath
            });

            File.WriteAllText(LastRunPath, settingsAsJson);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void ComparisonResults_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var identicalFiles = 0;
            var targetsMissing = 0;
            var newerOriginals = 0;
            var newerTargets = 0;

            foreach (var result in _comparisonResults)
            {
                switch (result.Status)
                {
                    case MatchStatus.FilesAreTheSame:
                        identicalFiles++;
                        break;
                    case MatchStatus.MissingAndCreatedAfterFolder:
                    case MatchStatus.MissingAndCreatedBeforeFolder:
                        targetsMissing++;
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
            ShowMissing.Content = $"Missing ({targetsMissing})";
            ShowOriginalNewer.Content = $"Original Newer ({newerOriginals})";
            ShowTargetNewer.Content = $"Target Newer ({newerTargets})";
            Total.Content = $"{_comparisonResults.Count} file(s)";
        }

        private async void RunCompare_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Folder1Path.FullName) || string.IsNullOrEmpty(Folder2Path.FullName))
                return;

            _comparisonResults.Clear();
            LoadProgress.Maximum = 0;
            LoadProgress.Value = 0;

            await GetFiles();

            ShowFilterChanged(null, null);
            LoadProgress.Value = 0;
        }

        private async Task GetFiles()
        {
            var folder1Path = Folder1Path.FullName;
            var folder2Path = Folder2Path.FullName;

            var searchService = new FileFinder(IgnoreFilePath, folder1Path);
            var originalFolderInfo = new DirectoryInfo(folder1Path);
            if (!originalFolderInfo.Exists) return;

            var tasks = new List<Task>();
            await foreach (var file in searchService.SearchDirectoryAsync(originalFolderInfo))
            {
                LoadProgress.Maximum++;
                tasks.Add(Task.Run(() =>
                {
                    var matchResult = GetMatchStatus(folder1Path, folder2Path, file);
                    Dispatcher.Invoke(() =>
                    {
                        _comparisonResults.Add(matchResult);
                        LoadProgress.Value++;
                    });
                }));
            }

            await Task.WhenAll(tasks);
        }

        private static ComparisonResult GetMatchStatus(string baseDirectory, string targetDirectory, FileInfo fileInfo)
        {
            var fileNameSplitIndex = baseDirectory.Length + 1;

            var comparison = new ComparisonResult
            {
                LeftName = fileInfo.FullName[fileNameSplitIndex..],
                LeftDate = fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                LeftSize = fileInfo.Length
            };

            var targetFileInfo = new FileInfo($"{targetDirectory}\\{comparison.LeftName}");

            if (!targetFileInfo.Exists)
            {
                comparison.Status = fileInfo.CreationTime < targetFileInfo.CreationTime
                    ? MatchStatus.MissingAndCreatedBeforeFolder
                    : MatchStatus.MissingAndCreatedAfterFolder;
                return comparison;
            }

            comparison.RightName = comparison.LeftName;
            comparison.RightDate = targetFileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            comparison.RightSize = targetFileInfo.Length;

            if (fileInfo.LastWriteTime > targetFileInfo.LastWriteTime.AddMinutes(1))
            {
                comparison.Status = MatchStatus.OriginalIsNewer;
                return comparison;
            }

            if (fileInfo.LastWriteTime < targetFileInfo.LastWriteTime.AddMinutes(-1))
            {
                comparison.Status = MatchStatus.TargetIsNewer;
                return comparison;
            }

            if (fileInfo.Length != targetFileInfo.Length)
            {
                comparison.Status = MatchStatus.FilesAreDifferent;
                return comparison;
            }

            comparison.Status = MatchStatus.FilesAreTheSame;
            return comparison;
        }

        private async void CopyFile(object sender, RoutedEventArgs e)
        {
            var filesToRemove = (from ComparisonResult item in Results.SelectedItems select item).ToList();
            var tasks = filesToRemove.Select(file => Task.Run(() =>
            {
                var originalPath = Folder1Path.FullName + "/" + file.LeftName;
                var targetPathInfo = new FileInfo(Folder2Path.FullName + "/" + file.LeftName);

                if (targetPathInfo.Directory != null && !targetPathInfo.Directory.Exists)
                    targetPathInfo.Directory.Create();
                File.Copy(originalPath, targetPathInfo.FullName, true);
                Dispatcher.Invoke(() => { _comparisonResults.Remove(file); });
            }));

            await Task.WhenAll(tasks);
        }

        private void IgnoreFileName(object sender, RoutedEventArgs e)
        {
            var filesToRemove = (from ComparisonResult item in Results.SelectedItems select item).ToList();

            foreach (var file in filesToRemove)
            {
                File.AppendAllText(IgnoreFilePath, "f: " + file.LeftName.Split('\\')[^1] + "\r");
                _comparisonResults.Remove(file);
            }
        }

        private void IgnoreFileFullPath(object sender, RoutedEventArgs e)
        {
            var filesToRemove = (from ComparisonResult item in Results.SelectedItems select item).ToList();

            foreach (var file in filesToRemove)
            {
                File.AppendAllText(IgnoreFilePath, "f: " + file.LeftName[1..] + "\r");
                _comparisonResults.Remove(file);
            }
        }

        private void IgnoreFolder(object sender, RoutedEventArgs e)
        {
            if (!(Results.SelectedItem is ComparisonResult result)) return;

            var fullPathInfo = new FileInfo(Folder1Path.FullName + "\\" + result.LeftName);
            if (fullPathInfo.Directory == null) return;
            var relativePath = fullPathInfo.Directory.FullName.Replace(Folder1Path.FullName, "");

            File.AppendAllText(IgnoreFilePath,
                "d: " + fullPathInfo.Directory.FullName.Replace(Folder1Path.Name, "")[1..] + "\r");
            _comparisonResults.Remove(result);
            foreach (var res in _comparisonResults.Where(r => r.LeftName.StartsWith(relativePath)).ToArray())
            {
                _comparisonResults.Remove(res);
            }
        }

        private void CompareFiles(object sender, RoutedEventArgs e)
        {
            if (!(Results.SelectedItem is ComparisonResult comparison)) return;

            var text1 = File.ReadAllText(Folder1Path.FullName + "\\" + comparison.LeftName);
            var text2 = File.ReadAllText(Folder2Path.FullName + "\\" + comparison.RightName);
            var textCompare = new TextCompare(text1, text2);
            textCompare.Show();
        }

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var result = Results.SelectedItem as ComparisonResult;
                Process.Start(Folder1Path.FullName + "\\" + result.LeftName);
            }
            catch
            {
                // ignored
            }
        }

        private void SearchBox_KeyUp(object sender, KeyEventArgs e) => ShowFilterChanged(null, null);

        private void ShowFilterChanged(object sender, RoutedEventArgs e)
        {
            if (Results?.Items != null)
                Results.Items.Filter = IsResultVisible;
        }

        private bool IsResultVisible(object obj)
        {
            var result = (ComparisonResult) obj;

            if (SearchBox.Text != "" && !result.LeftName.ToLower().Contains(SearchBox.Text.ToLower()))
                return false;

            return result.Status switch
            {
                MatchStatus.FilesAreTheSame when (ShowIdentical.IsChecked ?? false) => true,
                MatchStatus.MissingAndCreatedBeforeFolder when (ShowMissing.IsChecked ?? true) => true,
                MatchStatus.MissingAndCreatedAfterFolder when (ShowMissing.IsChecked ?? true) => true,
                MatchStatus.OriginalIsNewer when (ShowOriginalNewer.IsChecked ?? true) => true,
                MatchStatus.TargetIsNewer when (ShowTargetNewer.IsChecked ?? true) => true,
                _ => false
            };
        }

        private void IgnoreFilePath_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Choose Ignore File",
                Filter = "Ignore File (*.ignores)|*.ignores",
                InitialDirectory = IgnoresFolder
            };
            if (openFileDialog.ShowDialog() ?? false)
            {
                IgnoreFilePath = openFileDialog.FileName;
            }

            SaveSettings();
        }

        private void Folder1Path_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var result = LegacyFolderPicker.GetFolder("Select original folder");
            if (result != "")
            {
                Folder1Path = new DirectoryInfo(result);
            }
        }

        private void Folder2Path_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var result = LegacyFolderPicker.GetFolder("Select comparison folder");
            if (result != "")
            {
                Folder2Path = new DirectoryInfo(result);
            }
        }

        private void TestPath_OnClick(object sender, RoutedEventArgs e)
        {
            var window = new TestPathWindow(IgnoreFilePath, _path1.FullName);
            window.ShowDialog();
        }
    }
}