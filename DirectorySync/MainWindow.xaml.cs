using FileCompare;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SarmsMoveTo47;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DirectorySync
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string IgnoreFilePath { get; set; } = "";
        private string LastRunPath => SpecialDirectories.Temp + '\\' + "DirectorySync.lastrun";

        protected ObservableCollection<ComparisonResult> ComparisonResults = new ObservableCollection<ComparisonResult>();

        public MainWindow()
        {
            InitializeComponent();

            ComparisonResults.CollectionChanged += ComparisonResults_CollectionChanged;
            Results.ItemsSource = ComparisonResults;

            if (!File.Exists(LastRunPath))
            {
                FirstTimeSetup();
            }
            else
            {
                LoadSettings();
            }

        }

        private void LoadSettings()
        {
            var settingsFileText = File.ReadAllText(LastRunPath);
            var settings = JsonSerializer.Deserialize<PreviousRunSettings>(settingsFileText);
            Folder1Path.Text = settings.Folder1Path;
            Folder2Path.Text = settings.Folder2Path;
            IgnoreFilePath = settings.IgnorePath;
        }

        private void FirstTimeSetup()
        {
            Folder1Path_MouseDown(null, null);
            if (Folder1Path.Text == "")
                Application.Current.Shutdown();

            if (File.Exists(Folder1Path.Text + "\\" + ".ignores"))
            {
                IgnoreFilePath = Folder1Path.Text + "\\" + ".ignores";
            }
            else
            {
                IgnoreFilePath_MouseDown(null, null);
                if (IgnoreFilePath == "")
                    Application.Current.Shutdown();
            }

            Folder2Path_MouseDown(null, null);
            if (Folder2Path.Text == "")
                Application.Current.Shutdown();

            SaveSettings();
        }

        private void SaveSettings()
        {
            var settingsAsJson = JsonSerializer.Serialize(new PreviousRunSettings
            {
                Folder1Path = Folder1Path.Text,
                Folder2Path = Folder2Path.Text,
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
                if (result.Status == MatchStatus.TargetMissing)
                    targetsMissing++;

                if (result.Status == MatchStatus.OriginalIsNewer)
                    newerOriginals++;

                if (result.Status == MatchStatus.TargetIsNewer)
                    newerTargets++;
            }

            Total.Content = $"{ComparisonResults.Count} records. Targets Missing: {targetsMissing}. Newer Originals: {newerOriginals}. Newer Targets: {newerTargets}";
        }

        private async void RunCompare_Click(object sender, RoutedEventArgs e)
        {
            await GetFiles();
        }

        private async Task GetFiles()
        {
            ComparisonResults.Clear();
            LoadProgress.Maximum = 0;
            LoadProgress.Value = 0;

            var ignoreFilePath = this.IgnoreFilePath;
            var folder1Path = Folder1Path.Text;
            var folder2Path = Folder2Path.Text;

            var searchService = new FileFinderService(ignoreFilePath, folder1Path);
            var originalFolderInfo = new DirectoryInfo(folder1Path);
            if (!originalFolderInfo.Exists) return;

            var tasks = new List<Task>();
            await foreach (var file in searchService.GetFiles(originalFolderInfo))
            {
                var result = new ComparisonResult
                {
                    LeftName = file.FullName.Replace(Folder1Path.Text, ""),
                    LeftDate = file.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    LeftSize = file.Length
                };

                LoadProgress.Maximum++;

                tasks.Add(Task.Run(async () =>
                {
                    var matchResult = await GetMatchStatus(folder1Path, folder2Path, result);
                    if (matchResult.Status != MatchStatus.FilesAreTheSame)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ComparisonResults.Add(result);
                        });
                    }

                    Dispatcher.Invoke(() =>
                    {
                        LoadProgress.Value++;
                    });
                }));
            }
            await Task.WhenAll(tasks);
            LoadProgress.Value = 0;
        }

        private async Task<ComparisonResult> GetMatchStatus(string folder1, string folder2, ComparisonResult comparison)
        {
            var originalFileInfo = new FileInfo($"{folder1}\\{comparison.LeftName}");
            var targetFileInfo = new FileInfo($"{folder2}\\{comparison.LeftName}");

            if (!targetFileInfo.Exists)
            {
                comparison.Status = MatchStatus.TargetMissing;
                return comparison;
            }

            comparison.RightName = targetFileInfo.FullName.Replace(folder2, "");
            comparison.RightDate = targetFileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss");
            comparison.RightSize = targetFileInfo.Length;

            using var fs1 = new FileStream(originalFileInfo.FullName, FileMode.Open, FileAccess.Read);
            using var fs2 = new FileStream(targetFileInfo.FullName, FileMode.Open, FileAccess.Read);

            if (fs1.Length != fs2.Length)
            {
                comparison.Status = MatchStatus.FilesAreDifferent;
                return comparison;
            }

            int file1byte = 0;
            int file2byte = 0;
            while ((file1byte == file2byte) && file1byte != -1)
            {
                file1byte = fs1.ReadByte();
                file2byte = fs2.ReadByte();
                if (file1byte != file2byte)
                {
                    if ((originalFileInfo.LastWriteTimeUtc - targetFileInfo.LastWriteTimeUtc).TotalMinutes > 1)
                    {
                        comparison.Status = MatchStatus.OriginalIsNewer;
                        return comparison;
                    }

                    if ((originalFileInfo.LastWriteTimeUtc - targetFileInfo.LastWriteTimeUtc).TotalMinutes < 1)
                    {
                        comparison.Status = MatchStatus.TargetIsNewer;
                        return comparison;
                    }
                }
            }

            comparison.Status = MatchStatus.FilesAreTheSame;
            return comparison;
        }

        private void CopyFile(object sender, RoutedEventArgs e)
        {
            var filesToRemove = new List<ComparisonResult>();
            foreach (var item in Results.SelectedItems)
            {
                filesToRemove.Add(item as ComparisonResult);
            }

            foreach (var file in filesToRemove)
            {
                string originalPath = Folder1Path.Text + "/" + file.LeftName;
                FileInfo targetPathInfo = new FileInfo(Folder2Path.Text + "/" + file.LeftName);
                if (!targetPathInfo.Directory.Exists)
                    targetPathInfo.Directory.Create();

                File.Copy(originalPath, targetPathInfo.FullName, true);
                ComparisonResults.Remove(file);
            }
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
                File.AppendAllText(IgnoreFilePath, "f: " + file.LeftName[1..] + "\r");
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
                File.AppendAllText(IgnoreFilePath, "f: " + file.LeftName.Split('\\')[^1] + "\r");
                ComparisonResults.Remove(file);
            }
        }

        private void IgnoreFolder(object sender, RoutedEventArgs e)
        {
            var result = Results.SelectedItem as ComparisonResult;
            var fullPathInfo = new FileInfo(Folder1Path.Text + "\\" + result.LeftName);
            var relativePath = fullPathInfo.Directory.FullName.Replace(Folder1Path.Text, "");

            File.AppendAllText(IgnoreFilePath, "d: " + fullPathInfo.Directory.FullName.Replace(Folder1Path.Text, "")[1..] + "\r");
            ComparisonResults.Remove(result);
            foreach (var res in ComparisonResults.Where(result => result.LeftName.StartsWith(relativePath)).ToArray())
            {
                ComparisonResults.Remove(res);
            }
        }

        private void CompareFiles(object sender, RoutedEventArgs e)
        {
            var comparison = Results.SelectedItem as ComparisonResult;

            var text1 = File.ReadAllText(Folder1Path.Text + "\\" + comparison.LeftName);
            var text2 = File.ReadAllText(Folder2Path.Text + "\\" + comparison.RightName);
            var textCompare = new TextCompare(text1, text2);
            textCompare.Show();
        }

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var result = Results.SelectedItem as ComparisonResult;
                Process.Start(Folder1Path.Text + "\\" + result.LeftName);
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

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

            if ((ShowMissing.IsChecked ?? false) && result.Status == MatchStatus.TargetMissing)
                return true;

            if ((ShowOriginalNewer.IsChecked ?? false) && result.Status == MatchStatus.OriginalIsNewer)
                return true;

            if ((ShowTargetNewer.IsChecked ?? false) && result.Status == MatchStatus.TargetIsNewer)
                return true;

            return false;
        }

        private void IgnoreFilePath_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Choose Ignore File";
            openFileDialog.Filter = "Ignore File (*.ignores)|*.ignores";
            var picked = openFileDialog.ShowDialog() ?? false;
            if (picked)
            {
                IgnoreFilePath = openFileDialog.FileName;
            }
        }

        private void Folder1Path_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var result = LegacyFolderPicker.GetFolder("Select original folder");
            if (result != "")
            {
                Folder1Path.Text = result;
            }
        }

        private void Folder2Path_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var result = LegacyFolderPicker.GetFolder("Select comparison folder");
            if (result != "")
            {
                Folder2Path.Text = result;
            }
        }
    }
}