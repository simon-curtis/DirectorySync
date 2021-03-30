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
using System.Threading;
using System.Threading.Channels;
using MahApps.Metro.IconPacks;
using Brushes = System.Windows.Media.Brushes;

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

        private CancellationTokenSource _cancellationTokenSource;
        
        public MainWindow()
        {
            InitializeComponent();

            InitialiseCancellationToken();
            
            ComparisonResults.CollectionChanged += ComparisonResults_CollectionChanged;
            Results.ItemsSource = ComparisonResults;

            if (File.Exists(LastRunPath)) LoadSettings();
            else FirstTimeSetup();
        }

        protected override void OnClosing(CancelEventArgs e) => SaveSettings();

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
            Directory.CreateDirectory(IgnoresFolder);

            IgnoreFilePath = IgnoresFolder + "default.ignores";
            if (!File.Exists(IgnoreFilePath))
                File.WriteAllText(IgnoreFilePath, "");

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

        private int _identicalFiles;
        private int _rightUnique;
        private int _leftUnique;
        private int _newerOriginals;
        private int _newerTargets;

        private void ComparisonResults_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (ComparisonResults.Count == 0)
            {
                _identicalFiles = 0;
                _rightUnique = 0;
                _leftUnique = 0;
                _newerOriginals = 0;
                _newerTargets = 0;
            }
            else if (e.OldItems != null)
            {
                foreach (ComparisonResult item in e.OldItems)
                {
                    switch (item.Status)
                    {
                        case MatchStatus.FilesAreTheSame:
                            _identicalFiles--;
                            break;
                        case MatchStatus.RightUnique:
                            _rightUnique--;
                            break;
                        case MatchStatus.LeftUnique:
                            _leftUnique--;
                            break;
                        case MatchStatus.LeftIsNewer:
                            _newerOriginals--;
                            break;
                        case MatchStatus.RightIsNewer:
                            _newerTargets--;
                            break;
                    }
                }
            }
            else if (e.NewItems != null)
            {
                foreach (ComparisonResult result in e.NewItems)
                {
                    switch (result.Status)
                    {
                        case MatchStatus.FilesAreTheSame:
                            _identicalFiles++;
                            break;
                        case MatchStatus.RightUnique:
                            _rightUnique++;
                            break;
                        case MatchStatus.LeftUnique:
                            _leftUnique++;
                            break;
                        case MatchStatus.LeftIsNewer:
                            _newerOriginals++;
                            break;
                        case MatchStatus.RightIsNewer:
                            _newerTargets++;
                            break;
                    }
                }
            }

            ShowIdentical.Content = $"Identical ({_identicalFiles})";

            ShowLeftUnique.Content = $"Left Unique ({_leftUnique})";
            ShowLeftNewer.Content = $"Left Newer ({_newerOriginals})";

            ShowRightUnique.Content = $"Right Unique ({_rightUnique})";
            ShowRightNewer.Content = $"Right Newer ({_newerTargets})";

            Total.Content = $"{ComparisonResults.Count} file(s)";
        }

        private bool _searching;

        private void InitialiseCancellationToken()
        {
            _cancellationTokenSource = new();
            _cancellationTokenSource.Token.Register(HandleSearchStart);
        }
        
        private void HandleSearchStart()
        {
            if (_searching)
            {
                _searching = false;
                RunCompare.Foreground = Brushes.Green;
                RunCompareIcon.Kind = PackIconIoniconsKind.PlayCircleiOS;
                InitialiseCancellationToken();
            }
            else
            {
                _searching = true;
                RunCompare.Foreground = Brushes.Red;
                RunCompareIcon.Kind = PackIconIoniconsKind.HandiOS;
                ComparisonResults.Clear();
            }
            
            LoadProgress.Maximum = 0;
            LoadProgress.Value = 0;
        }

        private async void RunCompare_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(LeftFolder.FullName) || string.IsNullOrEmpty(RightFolder.FullName))
                return;

            if (_searching)
            { 
                _cancellationTokenSource.Cancel();
                return;
            }
            
            try
            {
                HandleSearchStart();
                await StartSearch(_cancellationTokenSource.Token);
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

        private enum FileSide
        {
            Left,
            Right
        }
        
        private async Task StartSearch(CancellationToken cancellationToken)
        {
            if (!LeftFolder.Exists || !RightFolder.Exists) return;

            LoadProgress.Value = 0;

            var fileGroupChannel = Channel.CreateUnbounded<(string key, FileInfo file, FileSide side)>();

            _ = Task.Run(async delegate
            {
                var searchTasks = new[]
                    {
                        (Folder: LeftFolder, Side: FileSide.Left), 
                        (Folder: RightFolder, Side: FileSide.Right)
                    }
                    .Select(g => Task.Run(async () =>
                    {
                        var relativeIndex = g.Folder.FullName.Length + 1;
                        await foreach (var file in Finder.SearchDirectoryAsync(
                            relativeIndex, g.Folder, cancellationToken))
                        {
                            var key = file.FullName[relativeIndex..];
                            await fileGroupChannel.Writer.WriteAsync((key, file, g.Side), cancellationToken);
                        }
                    }, cancellationToken));
                
                await Task.WhenAll(searchTasks);
                fileGroupChannel.Writer.Complete();
            }, cancellationToken);
            
            var comparisonChannel = Channel.CreateUnbounded<ComparisonResult>();
            
            _ = Task.Run(async delegate
            {
                var fileGroups = new Dictionary<string, FileGroup>(new StringComparerIgnoreCase());

                await foreach (var (key, file, side) in fileGroupChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    if (!fileGroups.ContainsKey(key))
                        fileGroups[key] = new FileGroup();
                    
                    switch (side)
                    {
                        case FileSide.Left:
                            fileGroups[key].Left = file;
                            break;

                        case FileSide.Right:
                            fileGroups[key].Right = file;
                            break;
                    }
                }

                foreach (var (key, group) in fileGroups)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    Dispatcher.Invoke(delegate { LoadProgress.Maximum++; });
                    var comparisonResult = GetComparionResult(group, key);
                    await comparisonChannel.Writer.WriteAsync(comparisonResult, cancellationToken);
                }

                comparisonChannel.Writer.Complete();
            }, cancellationToken);

            await foreach (var comparison in comparisonChannel.Reader.ReadAllAsync(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                ComparisonResults.Add(comparison);
                LoadProgress.Value++;
                DoEvents();
            }
        }

        private static void DoEvents()
        {
            static object? ExitFrame(object f)
            {
                ((DispatcherFrame) f).Continue = false;
                return null;
            }

            DispatcherFrame frame = new();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        private static MatchStatus GetStatus(FileSystemInfo left, FileSystemInfo right)
        {
            return (left.LastWriteTime - right.LastWriteTime).TotalSeconds switch
            {
                < -1 => MatchStatus.RightIsNewer,
                > 1 => MatchStatus.LeftIsNewer,
                _ => MatchStatus.FilesAreTheSame,
            };
        }

        private static ComparisonResult GetComparionResult(FileGroup group, string key)
        {
            var (left, right) = group;
            return (left, right) switch
            {
                ({ } l, null) => new ComparisonResult
                {
                    Name = key,
                    LeftDate = l.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    LeftSize = l.Length,
                    Status = MatchStatus.LeftUnique
                },
                (null, { } r) => new ComparisonResult
                {
                    Name = key,
                    RightDate = r.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    RightSize = r.Length,
                    Status = MatchStatus.RightUnique
                },
                ({ } l, { } r) => new ComparisonResult
                {
                    Name = key,
                    LeftDate = left!.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    LeftSize = left.Length,
                    RightDate = right!.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    RightSize = right.Length,
                    Status = GetStatus(l, r)
                },
                _ => new ComparisonResult
                {
                    Name = "Missing"
                }
            };
        }

        private void ShowFilterChanged(object sender, RoutedEventArgs e) => FilterList();

        private void FilterList()
        {
            if (Results?.Items != null)
                Results.Items.Filter = IsResultVisible;
        }
        
        private bool IsResultVisible(object obj)
        {
            var result = (ComparisonResult) obj;

            if (SearchBox.Text != "" && !result.Name.ToLower().Contains(SearchBox.Text.ToLower()))
                return false;

            return result.Status switch
            {
                MatchStatus.NotProcessed => true,
                MatchStatus.FilesAreTheSame when (ShowIdentical.IsChecked ?? false) => true,
                MatchStatus.RightUnique when (ShowRightUnique.IsChecked ?? true) => true,
                MatchStatus.LeftUnique when (ShowLeftUnique.IsChecked ?? true) => true,
                MatchStatus.LeftIsNewer when (ShowLeftNewer.IsChecked ?? true) => true,
                MatchStatus.RightIsNewer when (ShowRightNewer.IsChecked ?? true) => true,
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
                            CopyFile(comparison, RightFolder, LeftFolder);
                            comparison.LeftDate = comparison.RightDate;
                            comparison.LeftSize = comparison.RightSize;
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
                            CopyFile(comparison, LeftFolder, RightFolder);
                            comparison.RightDate = comparison.LeftDate;
                            comparison.RightSize = comparison.LeftSize;
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

            Results.ItemsSource = ComparisonResults;
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
        }

        private void Folder2Path_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var result = LegacyFolderPicker.GetFolder("Select comparison folder");
            if (result != "") RightFolder = new DirectoryInfo(result);
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

            foreach (ComparisonResult t in ComparisonResults)
            {
                var currentLeftDate = t.LeftDate;
                var currentLeftSize = t.LeftSize;

                t.Name = t.Name;
                t.LeftDate = t.RightDate;
                t.LeftSize = t.RightSize;
                t.RightDate = currentLeftDate;
                t.RightSize = currentLeftSize;
                t.Status = t.Status switch
                {
                    MatchStatus.RightUnique => MatchStatus.LeftUnique,
                    MatchStatus.LeftUnique => MatchStatus.RightUnique,
                    MatchStatus.LeftIsNewer => MatchStatus.RightIsNewer,
                    MatchStatus.RightIsNewer => MatchStatus.LeftIsNewer,
                    _ => t.Status
                };
                t.Resolution = t.Resolution switch
                {
                    ResolutionAction.CopyLeft => ResolutionAction.CopyRight,
                    ResolutionAction.CopyRight => ResolutionAction.CopyLeft,
                    _ => t.Resolution
                };
            }

            Results.ItemsSource = null;
            Results.ItemsSource = ComparisonResults;

            SaveSettings();
        }

        #endregion
    }

    public class StringComparerIgnoreCase : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => 
            string.Equals(x, y, StringComparison.InvariantCultureIgnoreCase);

        public int GetHashCode(string obj) => obj.ToLowerInvariant().GetHashCode();
    }

    internal class FileGroup
    {
        public FileInfo? Left { get; set; }
        public FileInfo? Right { get; set; }

        public override bool Equals(object obj)
        {
            return obj is FileGroup other &&
                   EqualityComparer<FileInfo>.Default.Equals(Left, other.Left) &&
                   EqualityComparer<FileInfo>.Default.Equals(Right, other.Right);
        }

        public override int GetHashCode() => HashCode.Combine(Left, Right);

        public void Deconstruct(out FileInfo left, out FileInfo right)
        {
            left = this.Left;
            right = this.Right;
        }

        public static implicit operator (FileInfo left, FileInfo right)(FileGroup value)
        {
            return (value.Left, value.Right);
        }
    }
}