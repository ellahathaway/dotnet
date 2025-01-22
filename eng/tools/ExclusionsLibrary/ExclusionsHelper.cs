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
    /// Suffix to use when no suffix is provided.
    /// Needed when using suffixes to indicate that the exclusion applies to all suffixes.
    /// </summary>
    private const string NullSuffix = "NULL_SUFFIX";

    /// <summary>
    /// Regex to narrow down the scope of exclusions. If null, all exclusions will be used.
    /// For instance, setting this to "vstest" will consider 
    /// "src/vstest/file.txt" but not "src/arcade/file.txt".
    /// </summary>
    private readonly Regex? _exclusionRegex;

    /// <summary>
    /// Specifies whether we are using suffixes to narrow down the scope of exclusions.
    /// </summary>
    private readonly bool _usingSuffixes;

    /// <summary>
    /// Storage for exclusions that were used during the test run.
    /// Used to check if a file matches an exclusion.
    /// </summary>
    private readonly ExclusionStorage _storage;

    /// <summary>
    /// Storage for exclusions that were not used during the test run.
    /// Used to generate a new baseline file with the exclusions that were used.
    /// </summary>
    private readonly ExclusionStorage _unusedStorage;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExclusionsHelper"/> class.
    /// <param name="exclusionsFilePath">Path to the exclusions file.</param>
    /// <param name="exclusionRegexString">Optional regex to narrow down the scope of exclusions.</param>
    /// <param name="usingSuffixes">Optional flag to specify whether we are using suffixes to narrow down the scope of exclusions.</param>
    /// </summary>
    public ExclusionsHelper(string exclusionsFilePath, string? exclusionRegexString = null, bool usingSuffixes = false)
    {
        _exclusionRegex = string.IsNullOrWhiteSpace(exclusionRegexString) ? null : new Regex(exclusionRegexString);
        _usingSuffixes = usingSuffixes;
        _storage = new ExclusionStorage(_usingSuffixes);
        ParseExclusionsFile(exclusionsFilePath);
        _unusedStorage = new ExclusionStorage(usingSuffixes);
    }

    /// <summary>
    /// Checks if a file is excluded.
    /// <param name="filePath">Path to the file to check.</param>
    /// <param name="suffix">
    ///     Optional suffix to narrow down the scope of exclusions.
    ///     If using suffices, the NULL_SUFFIX will be checked if a suffix is not provided
    ///     or if the file is not excluded with the provided suffix.
    /// </param>
    /// </summary>
    public bool IsFileExcluded(string filePath, string? suffix = null) =>
        CheckAndRemoveIfExcluded(filePath, suffix) || (_usingSuffixes && suffix != null && CheckAndRemoveIfExcluded(filePath, NullSuffix));

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
            IEnumerable<string> newLines;

            // anything in unusedStorage is not used, so we can remove it
            if (!_usingSuffixes)
            {
                // these are just basic lines, so remove the ones that are in the unused storage
                newLines = originalLines.Where(line =>
                    {
                        var (exclusion, _) = SplitExclusionLine(line);
                        return !_unusedStorage.Contains(file, null, exclusion);
                    }
                );
            }
            else
            {
                // these are lines with suffixes. We need to remove the unused suffixes. if all suffixes are unused, remove the line
                newLines = originalLines.Select(line =>
                {
                    var (exclusion, suffixes) = SplitExclusionLine(line);

                    if (suffixes is null)
                    {
                        throw new InvalidOperationException("Suffixes must be provided when using suffixes.");
                    }

                    IEnumerable<string> unusedSuffixes = _unusedStorage.GetSuffixes(file).Where(suffix => _unusedStorage.Contains(file, suffix, exclusion));
                    if (unusedSuffixes.Count() == suffixes.Count())
                    {
                        return null;
                    }

                    IEnumerable<string> newSuffixes = suffixes.Except(unusedSuffixes).ToList();
                    return $"{exclusion}|{string.Join(",", newSuffixes)}";
                })
                .Select(line => line!);
            }

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
    /// <param name="suffix">Optional suffix to narrow down the scope of exclusions.</param>
    /// </summary>
    private bool CheckAndRemoveIfExcluded(string filePath, string? suffix = null)
    {
        if (_storage.ContainsExclusionMatch(filePath, suffix, out var match))
        {
            _unusedStorage.Remove(match.file, match.suffix, match.exclusion);
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
        if (_storage.Contains(file, null, null))
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
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
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
        var (exclusion, suffixes) = SplitExclusionLine(line);

        if(_exclusionRegex is not null && !_exclusionRegex.IsMatch(exclusion))
        {
            // Exclusion does not match the exclusion regex, so we can skip it
            return;
        }

        if (_usingSuffixes)
        {
            if (suffixes is null)
            {
                throw new InvalidOperationException("Suffixes must be provided when using suffixes.");
            }

            foreach (string suffix in suffixes)
            {
                _storage.Add(file, suffix, exclusion);
            }
        }
        else
        {
            _storage.Add(file, null, exclusion);
        }
    }

    /// <summary>
    /// Splits a line from the exclusions file into an exclusion and its suffixes.
    /// If suffixes are not being used, the suffixes will be null.
    /// <param name="line">Line from the exclusions file.</param>
    /// </summary>
    private (string, IEnumerable<string>?) SplitExclusionLine(string line)
    {
        // We still split on '|' in case we have suffixes but we want to ignore them
        string[] parsedLine = line.Split("#")[0].Split('|');

        string exclusion = parsedLine[0].Trim();

        if (!_usingSuffixes)
        {
            return (exclusion, null);
        }

        IEnumerable<string> suffixes = parsedLine.Length == 1
            ? new[] { NullSuffix }
            : parsedLine[1].Split(',').Select(suffix => suffix.Trim());

        return (exclusion, suffixes);
    }
}
