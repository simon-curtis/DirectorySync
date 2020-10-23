using FileCompare;
using SarmsMoveTo47;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace DirectorySync
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<ComparisonResult> ComparisonResults = new ObservableCollection<ComparisonResult>();

        public MainWindow()
        {
            InitializeComponent();

            ComparisonResults.CollectionChanged += ComparisonResults_CollectionChanged;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }

        private void ComparisonResults_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Total.Content = $"{ComparisonResults.Count} records. Missing: {ComparisonResults.Count(r => r.Status == MatchStatus.TargetMissing)}. Updated: {ComparisonResults.Count(r => r.Status == MatchStatus.OriginalIsNewer)}";
        }

        private async void RunCompare_Click(object sender, RoutedEventArgs e)
        {
            ComparisonResults.Clear();
            Results.ItemsSource = ComparisonResults;

            var searchService = new FileFinderService(IgnoreFilePath.Text, Folder1Path.Text);
            var originalFolderInfo = new DirectoryInfo(Folder1Path.Text);
            if (!originalFolderInfo.Exists) return;

            var files = from file in searchService.GetFiles(originalFolderInfo)
                        select new ComparisonResult
                        {
                            LeftName = file.FullName.Replace(Folder1Path.Text, ""),
                            LeftDate = file.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                            LeftSize = file.Length
                        };

            foreach (var file in files)
            {
                var processedFile = await GetMatchStatus(Folder1Path.Text, Folder2Path.Text, file);
                if (processedFile.Status != MatchStatus.FilesAreTheSame)
                {
                    ComparisonResults.Add(processedFile);
                }
            }
        }

        private async Task<ComparisonResult> GetMatchStatus(string folder1, string folder2, ComparisonResult comparison)
        {
            var originalFileInfo = new FileInfo($"{folder1}\\{comparison.LeftName}");
            var targetFileInfo = new FileInfo($"{folder2}\\{comparison.LeftName}");

            if (targetFileInfo.Exists)
            {
                comparison.RightName = targetFileInfo.FullName.Replace(folder2, "");
                comparison.RightDate = targetFileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss");
                comparison.RightSize = targetFileInfo.Length;

                if (originalFileInfo.LastWriteTimeUtc > targetFileInfo.LastWriteTimeUtc)
                {
                    comparison.Status = MatchStatus.OriginalIsNewer;
                }
                //else if (originalFileInfo.Length != targetFileInfo.Length)
                //{
                //    comparison.Status = MatchStatus.FilesAreDifferent;
                //}
                else
                {
                    comparison.Status = MatchStatus.FilesAreTheSame;
                }
            }
            else
            {
                comparison.Status = MatchStatus.TargetMissing;
            }

            return comparison;
        }

        private void CopyFile(object sender, RoutedEventArgs e)
        {
            var result = Results.SelectedItem as ComparisonResult;
            MessageBox.Show(result.LeftName);
        }

        private void IgnoreFile(object sender, RoutedEventArgs e)
        {
            var filesToRemove = new List<ComparisonResult>();
            foreach (var item in Results.SelectedItems)
            {
                filesToRemove.Add(item as ComparisonResult);
            }

            foreach (var file in filesToRemove)
            {
                File.AppendAllText(IgnoreFilePath.Text, "f: " + file.LeftName[1..] + "\r");
                ComparisonResults.Remove(file);
            }
        }

        private void IgnoreFolder(object sender, RoutedEventArgs e)
        {
            var result = Results.SelectedItem as ComparisonResult;
            var fullPathInfo = new FileInfo(Folder1Path.Text + "\\" + result.LeftName);
            var relativePath = fullPathInfo.Directory.FullName.Replace(Folder1Path.Text, "");

            File.AppendAllText(IgnoreFilePath.Text, "d: " + fullPathInfo.Directory.FullName.Replace(Folder1Path.Text, "")[1..] + "\r");
            ComparisonResults.Remove(result);
            foreach (var res in ComparisonResults.Where(result => result.LeftName.StartsWith(relativePath)).ToArray())
            {
                ComparisonResults.Remove(res);
            }
        }


        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var result = Results.SelectedItem as ComparisonResult;
                Process.Start(Folder1Path.Text + "\\" + result.LeftName);
            } catch {}
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        
    }
}
