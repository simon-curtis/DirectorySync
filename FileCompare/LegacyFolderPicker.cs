namespace FileCompare
{
    public static class LegacyFolderPicker
    {
        public static string GetFolder(string title)
        {
            var openFileDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFileDialog.Description = title;
            var result = openFileDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                return openFileDialog.SelectedPath;
            }
            return "";
        }
    }
}
