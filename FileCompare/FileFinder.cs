using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FileCompare
{
    public class FileFinder
    {
        private readonly string _filterPath;
        public Regex DirectoryFilters { get; private set; }
        private ICollection<string> DirectoryPaths { get; set; } = new List<string>();
        public Regex FileFilters { get; private set; }
        private ICollection<string> FilePaths { get; set; } = new List<string>();

        public FileFinder(string filterPath)
        {
            _filterPath = filterPath;
            LoadFiltersFromFile();
        }
        
        private void LoadFiltersFromFile()
        {
            foreach (var line in File.ReadAllLines(_filterPath))
            {
                if (string.IsNullOrEmpty(line)) continue;

                var linePart = line[2..].Trim();

                switch (line[0])
                {
                    case 'd':
                        DirectoryPaths.Add(linePart);
                        break;

                    case 'f':
                        FilePaths.Add(linePart);
                        break;
                }
            }
            SetRegexFilters();
        }

        private void SetRegexFilters()
        {
            static Regex CreateRegexString(ICollection<string> paths, Func<string, string> regexTemplate) =>
                new Regex(string.Join('|', paths.Select(regexTemplate)), RegexOptions.IgnoreCase);

            DirectoryFilters = CreateRegexString(DirectoryPaths, path => $@"({path})");
            FileFilters = CreateRegexString(FilePaths, path => $@"({path}$)");
        }

        public async IAsyncEnumerable<FileInfo> SearchDirectoryAsync(int relativePathIndex, DirectoryInfo directoryInfo, 
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var file in directoryInfo.GetFiles())
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                if (FileFilters.IsMatch(file.FullName[relativePathIndex..])) continue;
                yield return file;
            }

            var subFileChannel = Channel.CreateUnbounded<FileInfo>();

            _ = Task.Run(async delegate
            {
                var tasks = new List<Task>();

                var directoriesToSearch = directoryInfo.GetDirectories()
                    .Where(dir => !DirectoryFilters.IsMatch(dir.FullName[relativePathIndex..]));
                
                foreach (var dir in directoriesToSearch.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
                {
                    tasks.Add(Task.Run(async delegate
                    {
                        await foreach (var file in SearchDirectoryAsync(relativePathIndex, dir, cancellationToken))
                            await subFileChannel.Writer.WriteAsync(file, cancellationToken);
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks);

                subFileChannel.Writer.Complete();
            }, cancellationToken);

            await foreach (var file in subFileChannel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return file;
            }
        }

        /// <summary>
        /// Adds the relative path to the directory filter list, this method will escape the string
        /// </summary>
        /// <param name="relativePath">Unescaped directory path</param>
        /// <example>"\data\settings"</example>
        public void IgnoreFolderPath(string relativePath)
        {
            DirectoryPaths.Add(Regex.Escape(relativePath));
            SetRegexFilters();
        }
        
        /// <summary>
        /// Adds the relative path to the file filter list, this method will escape the string
        /// </summary>
        /// <param name="relativePath">Unescaped file path</param>
        /// <example>"\data\settings\setting.dat"</example>
        public void IgnoreFilePath(string relativePath)
        {
            FilePaths.Add(Regex.Escape(relativePath));
            SetRegexFilters();
        }
        
        /// <summary>
        /// Adds the file name to the file filter list, this method will escape the string
        /// </summary>
        /// <param name="fileName">Unescaped file name</param>
        /// <example>"setting.dat"</example>
        public void IgnoreFileName(string fileName)
        {
            FilePaths.Add(Regex.Escape(fileName));
            SetRegexFilters();
        }
        
        public (IEnumerable<string> directoryExlusions, IEnumerable<string> fileExclusions) TestFile(string basePath, string filePath)
        {
            var fileInfo = new FileInfo(filePath);

            if (!fileInfo.Exists)
                return (Array.Empty<string>(), Array.Empty<string>());

            var subDirectoryPath = fileInfo.DirectoryName?.Replace(basePath, "");

            var directoryExlusions = string.IsNullOrEmpty(subDirectoryPath)  
                ? Array.Empty<string>()
                : DirectoryFilters
                    .Matches(subDirectoryPath)
                    .Select(match => $"Directory: {match.Value}");

            var fileExclusions = FileFilters.Matches(fileInfo.Name).Select(match => $"File: {match.Value}");

            return (directoryExlusions, fileExclusions);
        }

        public string SaveFilters()
        {
            try
            {
                var sb = new StringBuilder();

                foreach (var line in DirectoryPaths.OrderBy(path => path).Select(path => $"d: {path}"))
                    sb.AppendLine(line);

                foreach (var line in FilePaths.OrderBy(path => path).Select(path => $"f: {path}"))
                    sb.AppendLine(line);

                File.WriteAllText(_filterPath, sb.ToString());
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            return "filters saved successfully";
        }

        public void ResetFilters()
        {
            this.LoadFiltersFromFile();
        }
    }
}