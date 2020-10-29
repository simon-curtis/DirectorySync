using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SarmsMoveTo47
{
    public class FileFinderService
    {
        private readonly Regex _directoryFilters;
        private readonly Regex _fileFilters;
        private readonly string _originalFolderPath;

        public FileFinderService(string filterPath, string originalFolderPath)
        {
            _originalFolderPath = originalFolderPath;

            var directoryFilters = new List<string>();
            var fileFilters = new List<string>();

            foreach (var line in File.ReadAllLines(filterPath))
            {
                if (string.IsNullOrEmpty(line)) continue;

                var linePart = Regex.Escape(line[2..].Trim()).Replace(@"\*", ".*");

                switch (line[0])
                {
                    case 'd':
                        directoryFilters.Add($@"({linePart}$)");
                        break;

                    case 'f':
                        fileFilters.Add($@"({linePart}$)");
                        break;
                }
            }

            _directoryFilters = new Regex(string.Join('|', directoryFilters), RegexOptions.IgnoreCase);
            _fileFilters = new Regex(string.Join('|', fileFilters), RegexOptions.IgnoreCase);
        }

        public async IAsyncEnumerable<FileInfo> GetFiles(DirectoryInfo directoryInfo)
        {
            string relativeFolderPath = directoryInfo.FullName.Replace(_originalFolderPath, "");
            
            if (!_directoryFilters.IsMatch(relativeFolderPath))
            {
                foreach (var subDir in directoryInfo.GetDirectories())
                {
                    await foreach (var file in GetFiles(subDir))
                    {
                        yield return file;
                    }
                }

                foreach (var file in directoryInfo.GetFiles())
                {
                    string relativeFilePath = file.FullName.Replace(_originalFolderPath, "");
                    if (_fileFilters.IsMatch(relativeFilePath)) continue;
                    yield return file;
                }
            }
        }
    }
}