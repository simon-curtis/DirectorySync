using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileCompare
{
    public class FileFinder
    {
        private readonly Regex _directoryFilters;
        private readonly Regex _fileFilters;
        private readonly string _originalFolderPath;

        public FileFinder(string filterPath, string originalFolderPath)
        {
            _originalFolderPath = originalFolderPath;

            var directoryFilters = new List<string>();
            var fileFilters = new List<string>();

            foreach (var line in File.ReadAllLines(filterPath))
            {
                if (string.IsNullOrEmpty(line)) continue;

                var linePart = line[2..].Trim();

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

        private bool IncludeDirectory(DirectoryInfo subDir) => !_directoryFilters.IsMatch(subDir.FullName.Replace(_originalFolderPath, ""));
        private bool IncludeFile(FileInfo file) => !_fileFilters.IsMatch(file.Name);

        public async IAsyncEnumerable<FileInfo> SearchDirectoryAsync(DirectoryInfo directoryInfo)
        {
            foreach (var file in directoryInfo.GetFiles().Where(IncludeFile))
                yield return file;
            
            foreach (var dir in directoryInfo.GetDirectories().Where(IncludeDirectory))
                await foreach (var file in SearchDirectoryAsync(dir))
                    yield return file;
        }

        public (IEnumerable<string> directoryExlusions, IEnumerable<string> fileExclusions) TestFile(string filePath)
        {
            if (!filePath.Contains(_originalFolderPath))
                return (Array.Empty<string>(), Array.Empty<string>());

            var fileInfo = new FileInfo(filePath);

            if (!fileInfo.Exists)
                return (Array.Empty<string>(), Array.Empty<string>());

            var subDirectoryPath = fileInfo.DirectoryName?.Replace(_originalFolderPath, "");

            var directoryExlusions = string.IsNullOrEmpty(subDirectoryPath)  
                ? Array.Empty<string>()
                : _directoryFilters
                    .Matches(subDirectoryPath)
                    .Select(match => $"Directory: {match.Value}");

            var fileExclusions = _fileFilters.Matches(fileInfo.Name).Select(match => $"File: {match.Value}");

            return (directoryExlusions, fileExclusions);
        }
    }
}