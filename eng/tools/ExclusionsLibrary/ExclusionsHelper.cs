// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

class ExclusionsHelper
{
    /// <summary>
    /// Prefix for lines that import other exclusion files.
    /// </summary>
    private const string FileImportPrefix = "import:";

    /// <summary>
    /// Regex to narrow down the scope of exclusions. If null, all exclusions will be used.
    /// For instance, setting this to "vstest" will consider 
    /// "src/vstest/file.txt" but not "src/arcade/file.txt".
    /// </summary>
    private readonly Regex? _exclusionRegex;

    /// <summary>
    /// Storage for exclusions that were used during the test run.
    /// Used to check if a file matches an exclusion.
    /// </summary>
    private readonly ExclusionsStorage _storage;

    /// <summary>
    /// Storage for exclusions that were not used during the test run.
    /// Used to generate a new baseline file with the exclusions that were used.
    /// </summary>
    private readonly ExclusionsStorage _unusedStorage;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExclusionsHelper"/> class.
    /// <param name="exclusionsFilePath">Path to the exclusions file.</param>
    /// <param name="exclusionRegexString">Optional regex to narrow down the scope of exclusions.</param>
    /// </summary>
    public ExclusionsHelper(string exclusionsFilePath, string? exclusionRegexString = null)
    {
        _exclusionRegex = string.IsNullOrWhiteSpace(exclusionRegexString) ? null : new Regex(exclusionRegexString);
        _usingSuffixes = usingSuffixes;
        _storage = new ExclusionsStorage();
        ParseExclusionsFile(exclusionsFilePath);
        _unusedStorage = new ExclusionsStorage(_storage);
    }

    /// <summary>
    /// Checks if a file is excluded.
    /// <param name="filePath">Path to the file to check.</param>
    /// <param name="suffix">
    ///     Optional suffix to narrow down the scope of exclusions.
    ///     If using suffices, patterns without any suffixes will also
    ///     be checked if the file is not excluded with the provided suffix.
    /// </param>
    /// </summary>
    public bool IsFileExcluded(string filePath, string? suffix = null) =>
        CheckAndRemoveIfExcluded(filePath, suffix) || (suffix != null && CheckAndRemoveIfExcluded(filePath, null));

    /// <summary>
    /// Generates a new baseline file with the exclusions that were used.
    /// <param name="updatedFileTag">Optional tag to append to the updated file name.</param>
    /// <param name="additionalLines">Optional additional lines to append to the updated file.</param>
    /// </summary>
    public void GenerateNewBaselineFile(string? updatedFileTag = null, List<string>? additionalLines = null)
    {
        // Get the keys of the exclusions file dictionary
        foreach (string file in _unusedStorage.GetFiles())
        {
            string[] originalLines = File.ReadAllLines(file);
            IEnumerable<string> newLines = originalLines.Select(line =>
            {
                if (IsIgnorableLine(line))
                {
                    return line;
                }

                Exclusion exclusion = new(line);
                Exclusion? unusedExclusion = _unusedStorage.GetExclusion(file, exclusion.GetPattern());
                if (unusedExclusion is not null)
                {
                    HashSet<string> unusedSuffixes = unusedExclusion.GetSuffixes();
                    if (unusedSuffixes.Count == exclusion.GetSuffixes().Count)
                    {
                        return null;
                    }

                    // remove the unused suffixes from the line
                    foreach (string suffix in unusedSuffixes)
                    {
                        string suffixPattern = $@"\s*,?\s*{suffix}\s*,?\s*";
                        line = Regex.Replace(line, suffixPattern.ToString(), string.Empty);
                    }
                }
                return line;
            });

             if (additionalLines is not null)
            {
                newLines = newLines.Concat(additionalLines);
            }

            string updatedFileName = updatedFileTag is null
                ? $"Updated{file}"
                : $"Updated{Path.GetFileNameWithoutExtension(file)}.{updatedFileTag}{Path.GetExtension(file)}";
        }
    }

    /// <summary>
    /// Checks if a file is excluded and removes it from the unused storage if it is.
    /// <param name="filePath">Path to the file to check.</param>
    /// <param name="suffix">Suffix to narrow down the scope of exclusions.</param>
    /// </summary>
    private bool CheckAndRemoveIfExcluded(string filePath, string? suffix)
    {
        if (_storage.HasMatch(filePath, suffix, out (string file, string pattern)? match))
        {
            _unusedStorage.Remove(match.file, match.pattern, suffix);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the exclusions file and adds the exclusions to the storage.
    /// <param name="file">Path to the exclusions file.</param>
    /// </summary>
    private void ParseExclusionsFile(string file)
    {
        if (_storage.Contains(file))
        {
            return;
        }

        if (!Path.IsPathFullyQualified(file))
        {
            throw new ArgumentException($"File path must be fully qualified: {file}");
        }

        if (!File.Exists(file))
        {
            throw new FileNotFoundException($"Exclusions file not found: {file}");
        }

        foreach (string line in File.ReadLines(file))
        {
            string trimmedLine = line.Trim();
            if (IsIgnorableLine(trimmedLine))
            {
                continue;
            }

            if (trimmedLine.StartsWith(FileImportPrefix))
            {
                string importFile = trimmedLine.Substring(FileImportPrefix.Length).Trim();
                if (!Path.IsPathFullyQualified(importFile))
                {
                    string directory = Path.GetDirectoryName(file) ?? throw new InvalidOperationException($"Could not get directory for file: {file}");
                    importFile = Path.Combine(directory, importFile);
                }

                ParseExclusionsFile(importFile);
            }
            else
            {
                ParseExclusionLine(file, trimmedLine);
            }
        }
    }

    /// <summary>
    /// Parses a line from the exclusions file and adds the exclusion
    /// (and suffixes if using suffixes) to the storage.
    /// <param name="file">Path to the exclusions file.</param>
    /// <param name="line">Line from the exclusions file.</param>
    /// </summary>
    private void ParseExclusionLine(string file, string line)
    {
        Exclusion exclusion = new Exclusion(line);
        if(_exclusionRegex is not null && !_exclusionRegex.IsMatch(exclusion.Pattern))
        {
            // Exclusion does not match the exclusion regex, so we can skip it
            return;
        }
        _storage.Add(file, exclusion);
    }

    private bool IsIgnorableLine(string line) =>
        string.IsNullOrWhiteSpace(line) || line.StartsWith("#");
}
