using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using FileCompare;

namespace DirectorySync
{
    public partial class TestPathWindow : Window
    {
        private string FilterPath { get; }
        private string OriginalPath { get; }

        public TestPathWindow(string filterPath, string originalPath)
        {
            FilterPath = filterPath;
            OriginalPath = originalPath;
            InitializeComponent();
        }

        private void RunTest_OnClick(object sender, RoutedEventArgs e)
        {
            PathToTest.Text = PathToTest.Text.Replace("\"", "");
            
            var finder = new FileFinder(FilterPath, OriginalPath);
            Results.Text = "";

            var (directoryFilters, fileFilters) = finder.TestFile(PathToTest.Text);
            Results.Text += string.Join("\r", directoryFilters);
            Results.Text += string.Join("\r", fileFilters);
        }
    }
}