using System.IO;
using System.Windows;
using FileCompare;

namespace DirectorySync
{
    public partial class TestPathWindow : Window
    {
        private string FilterPath { get; set; }

        public TestPathWindow(string filterPath)
        {
            FilterPath = filterPath;
            InitializeComponent();
        }

        private void RunTest_OnClick(object sender, RoutedEventArgs e)
        {
            var matches = FileFinderService.TestFile(FilterPath, PathToTest.Text);
            foreach (var match in matches)
            {
                Results.Text += match + "\r";
            }
        }
    }
}