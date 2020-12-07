using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

        private Task<List<FileInfo>> GetFiles(DirectoryInfo directory)
        {
            var files = new List<FileInfo>();
            foreach (var file in directory.GetFiles())
            {
                string relativeFilePath = file.FullName.Replace(_originalFolderPath, "");
                if (_fileFilters.IsMatch(relativeFilePath)) continue;
                    files.Add(file);
            }
            return Task.FromResult(files);
        } 


        private bool DirectoryHidden(DirectoryInfo subDir) => _directoryFilters.IsMatch(subDir.FullName.Replace(_originalFolderPath, ""));
        private bool FileHidden(FileInfo file) => _fileFilters.IsMatch(file.FullName.Replace(_originalFolderPath, ""));

        private async Task<IEnumerable<FileInfo>> SearchSubDirectoryAsync(DirectoryInfo Directory)
        {
            var files = new List<FileInfo>();
            await foreach (var file in SearchDirectoryAsync(Directory))
                files.Add(file);
            return files;
        }

        public async IAsyncEnumerable<FileInfo> SearchDirectoryAsync(DirectoryInfo directoryInfo)
        {
            var subDirs = directoryInfo.GetDirectories();
            var tasks = new List<Task>();
            var files = new List<FileInfo>();

            foreach (var subDir in subDirs) {
                if (DirectoryHidden(subDir)) continue;
                tasks.Add(Task.Run(async () => {
                    var _files = await SearchSubDirectoryAsync(subDir);
                    files.AddRange(_files);
                }));
            }
            await Task.WhenAll(tasks);

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var file in directoryInfo.GetFiles())
            {
                if (FileHidden(file)) continue;
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