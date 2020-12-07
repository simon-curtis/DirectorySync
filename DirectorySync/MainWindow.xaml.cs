using FileCompare;
using Microsoft.VisualBasic.CompilerServices;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SarmsMoveTo47;
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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DirectorySync
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string ingoresFilePath = "";

        private DirectoryInfo path1;
        private DirectoryInfo path2;

        private DateTime Folder1CreationDate;
        private DateTime Folder2CreationDate;

        private string IgnoreFilePath {
            get => ingoresFilePath;
            set { ingoresFilePath = value; IgnoreFilePathButton.Text = value.Split("\\")[^1].Replace(".ignores", ""); }
        }

        private DirectoryInfo Folder1Path
        {
            get => path1;
            set
            {
                path1 = value;
                Folder1PathTextBox.Text = value.FullName;
                Folder1CreationDate = value.CreationTime;
            }
        }

        private DirectoryInfo Folder2Path
        {
            get => path2;
            set
            {
                path2 = value;
                Folder2PathTextBox.Text = value.FullName;
                Folder2CreationDate = value.CreationTime;
            }
        }

        
        private string AppDataString => $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\DirectorySync";
        private string LastRunPath => $@"{AppDataString}\.lastrun";
        private string IgnoresFolder => $@"{AppDataString}\ignores\";

        protected ObservableCollection<ComparisonResult> ComparisonResults = new ObservableCollection<ComparisonResult>();

        public MainWindow()
        {
            InitializeComponent();

            ComparisonResults.CollectionChanged += ComparisonResults_CollectionChanged;
            Results.ItemsSource = ComparisonResults;

            if (File.Exists(LastRunPath)) LoadSettings(); else FirstTimeSetup();
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
            if (!Directory.Exists(IgnoresFolder)) {
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
            var targetsMissing = 0;
            var newerOriginals = 0;
            var newerTargets = 0;

            foreach (var result in ComparisonResults)
            {
                switch (result.Status)
                {
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

                    case MatchStatus.NotProcessed:
                    case MatchStatus.FilesAreDifferent:
                    case MatchStatus.FilesAreTheSame:
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            Total.Content = $"{ComparisonResults.Count} records.  Targets Missing: {targetsMissing}. Newer Originals: {newerOriginals}. Newer Targets: {newerTargets}";
        }

        private async void RunCompare_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Folder1Path.FullName) || string.IsNullOrEmpty(Folder2Path.FullName))
                return;

            await GetFiles();
        }

        private async Task GetFiles()
        {
            ComparisonResults.Clear();
            LoadProgress.Maximum = 0;
            LoadProgress.Value = 0;

            var folder1Path = Folder1Path.FullName;
            var folder2Path = Folder2Path.FullName;

            var searchService = new FileFinderService(IgnoreFilePath, folder1Path);
            var originalFolderInfo = new DirectoryInfo(folder1Path);
            if (!originalFolderInfo.Exists) return;

            var fileNameSplitIndex = Folder1Path.FullName.Length;
            var tasks = new List<Task>();
            await foreach (var file in searchService.SearchDirectoryAsync(originalFolderInfo))
            {
                var result = new ComparisonResult
                {
                    LeftName = file.FullName[fileNameSplitIndex..],
                    LeftDate = file.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    LeftSize = file.Length
                };

                LoadProgress.Maximum++;

                tasks.Add(Task.Run(() =>
                {
                    var matchResult = GetMatchStatus(folder1Path, folder2Path, result);
                    Dispatcher.Invoke(() =>
                    {
                        ComparisonResults.Add(matchResult);
                        LoadProgress.Value++;
                    });
                }));
            }
            await Task.WhenAll(tasks);
            ShowFilterChanged(null,null);
            LoadProgress.Value = 0;
        }

        private ComparisonResult GetMatchStatus(string folder1, string folder2, ComparisonResult comparison)
        {
            var originalFileInfo = new FileInfo($"{folder1}\\{comparison.LeftName}");
            var targetFileInfo = new FileInfo($"{folder2}\\{comparison.LeftName}");

            if (!targetFileInfo.Exists)
            {
                comparison.Status = originalFileInfo.CreationTime < Folder2CreationDate
                    ? MatchStatus.MissingAndCreatedBeforeFolder
                    : MatchStatus.MissingAndCreatedAfterFolder;
                return comparison;
            }

            comparison.RightName = targetFileInfo.FullName.Replace(folder2, "");
            comparison.RightDate = targetFileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            comparison.RightSize = targetFileInfo.Length;


            if (originalFileInfo.LastWriteTime.TimeOfDay > targetFileInfo.LastWriteTime.TimeOfDay)
            {
                comparison.Status = MatchStatus.OriginalIsNewer;
                return comparison;
            }

            if (originalFileInfo.LastWriteTime.TimeOfDay < targetFileInfo.LastWriteTime.TimeOfDay)
            {
                comparison.Status = MatchStatus.TargetIsNewer;
                return comparison;
            }

            if (originalFileInfo.Length != targetFileInfo.Length)
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
                Dispatcher.Invoke(() => { ComparisonResults.Remove(file); });
            }));

            await Task.WhenAll(tasks);
        }

        private void IgnoreFileName(object sender, RoutedEventArgs e)
        {
            var filesToRemove = new List<ComparisonResult>();
            foreach (var item in Results.SelectedItems)
            {
                filesToRemove.Add(item as ComparisonResult);
            }

            foreach (var file in filesToRemove)
            {
                File.AppendAllText(IgnoreFilePath, "f: " + file.LeftName.Split('\\')[^1] + "\r");
                ComparisonResults.Remove(file);
            }
        }

        private void IgnoreFileFullPath(object sender, RoutedEventArgs e)
        {
            var filesToRemove = new List<ComparisonResult>();
            foreach (var item in Results.SelectedItems)
            {
                filesToRemove.Add(item as ComparisonResult);
            }

            foreach (var file in filesToRemove)
            {
                File.AppendAllText(IgnoreFilePath, "f: " + file.LeftName[1..] + "\r");
                ComparisonResults.Remove(file);
            }
        }

        private void IgnoreFolder(object sender, RoutedEventArgs e)
        {
            if (!(Results.SelectedItem is ComparisonResult result)) return;

            var fullPathInfo = new FileInfo(Folder1Path.FullName + "\\" + result.LeftName);
            if (fullPathInfo.Directory == null) return;
            var relativePath = fullPathInfo.Directory.FullName.Replace(Folder1Path.FullName, "");

            File.AppendAllText(IgnoreFilePath, "d: " + fullPathInfo.Directory.FullName.Replace(Folder1Path.Name, "")[1..] + "\r");
            ComparisonResults.Remove(result);
            foreach (var res in ComparisonResults.Where(r => r.LeftName.StartsWith(relativePath)).ToArray())
            {
                ComparisonResults.Remove(res);
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
            {
                Results.Items.Filter = new Predicate<object>(IsResultVisible);
            }
        }

        private bool IsResultVisible(object obj)
        {
            var result = (ComparisonResult)obj;

            if (SearchBox.Text != "" && !result.LeftName.ToLower().Contains(SearchBox.Text.ToLower()))
                return false;

            return result.Status switch
            {
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
            var window = new TestPathWindow(IgnoreFilePath);
            window.ShowDialog();
        }
    }
}