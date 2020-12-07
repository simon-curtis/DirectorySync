using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FileCompare
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

        private bool DirectoryHidden(DirectoryInfo subDir) => _directoryFilters.IsMatch(subDir.FullName.Replace(_originalFolderPath, ""));
        private bool FileHidden(string fileName) => _fileFilters.IsMatch(fileName);

        public async IAsyncEnumerable<FileInfo> SearchDirectoryAsync(DirectoryInfo directoryInfo)
        {
            var subDirs = directoryInfo.GetDirectories();
            var files = new List<FileInfo>();

            var tasks = subDirs
                .Where(subDir => !DirectoryHidden(subDir))
                .Select(async subDir =>
                {
                    await foreach (var file in SearchDirectoryAsync(subDir)) 
                        files.Add(file);
                })
                .ToList();


            await Task.WhenAll(tasks);

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var file in directoryInfo.GetFiles())
            {
                if (FileHidden(file.Name)) continue;
                yield return file;
            }
        }

        public static IEnumerable<string> TestFile(string filterPath, string relativeFilePath)
        {
            var directoryFiltersStrings = new List<string>();
            var fileFiltersStrings = new List<string>();

            foreach (var line in File.ReadAllLines(filterPath))
            {
                if (string.IsNullOrEmpty(line)) continue;

                var linePart = line[2..].Trim();

                switch (line[0])
                {
                    case 'd':
                        directoryFiltersStrings.Add($@"({linePart}$)");
                        break;

                    case 'f':
                        fileFiltersStrings.Add($@"({linePart}$)");
                        break;
                }
            }

            var directoryFilters = new Regex(string.Join('|', directoryFiltersStrings), RegexOptions.IgnoreCase);
            var fileFilters = new Regex(string.Join('|', fileFiltersStrings), RegexOptions.IgnoreCase);

            var directory = string.Join("\\", relativeFilePath.Split('\\')[..^1]);
            var directoryExlusions = directoryFilters.Matches(directory).Select(match => $"{match.Name} {match.Value}");

            var filename = relativeFilePath.Split('\\')[^1];
            var fileExclusions = fileFilters.Matches(filename).Select(match => $"{match.Name} {match.Value}");

            return directoryExlusions.Concat(fileExclusions).ToArray();
        }
    }
}