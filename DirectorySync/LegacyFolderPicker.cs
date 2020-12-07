using System.Windows.Forms;

namespace DirectorySync
{
    public static class LegacyFolderPicker
    {
        public static string GetFolder(string title)
        {
            var openFileDialog = new FolderBrowserDialog
            {
                Description = title
            };
            return openFileDialog.ShowDialog() == DialogResult.OK 
                ? openFileDialog.SelectedPath 
                : "";
        }
    }
}
