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
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.SourceBuild.Tests;

internal class ExclusionsHelper
{
    private const string NullSuffix = "NULL_SUFFIX";

    private readonly string _baselineSubDir;

    // Use this to narrow down the scope of exclusions to a specific category.
    // For instance, setting this to "vstest" will consider 
    // "src/vstest/exclusions.txt" but not "src/arcade/exclusions.txt".
    private readonly Regex? _exclusionRegex;

    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _fileToSuffixToExclusions = new();

    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _fileToSuffixToUnusedExclusions = new();

    public ExclusionsHelper(string exclusionsFileName, string baselineSubDir = "", string? exclusionRegexString = null)
    {
        if (exclusionsFileName is null)
        {
            throw new ArgumentNullException(nameof(exclusionsFileName));
        }

        _baselineSubDir = baselineSubDir;
        _exclusionRegex = string.IsNullOrWhiteSpace(exclusionRegexString) ? null : new Regex(exclusionRegexString);

        ParseExclusionsFile(exclusionsFileName);

        _fileToSuffixToUnusedExclusions = new Dictionary<string, Dictionary<string, HashSet<string>>>(
            _fileToSuffixToExclusions.ToDictionary(pair => pair.Key, pair => new Dictionary<string, HashSet<string>>(
                pair.Value.ToDictionary(innerPair => innerPair.Key, innerPair => new HashSet<string>(innerPair.Value)))));
    }

    internal bool IsFileExcluded(string filePath, string suffix = NullSuffix)
    {
        if (suffix is null)
        {
            throw new ArgumentNullException(nameof(suffix));
        }

        // If a specific suffix is provided, check that first. If it is not found, check the default suffix.
        return CheckAndRemoveIfExcluded(filePath, suffix) ||
            (suffix != NullSuffix && CheckAndRemoveIfExcluded(filePath, NullSuffix));
    }

    /// <summary>
    /// Generates a new baseline file with the exclusions that were used during the test run.
    /// <param name="updatedFileTag">Optional tag to append to the updated file name.</param>
    /// <param name="additionalLines">Optional additional lines to append to the updated file.</param>
    /// </summary>
    internal void GenerateNewBaselineFile(string? updatedFileTag = null, List<string>? additionalLines = null)
    {
        // Get the keys of the exclusions file dictionary
        foreach (string exclusionsFile in _fileToSuffixToExclusions.Keys)
        {
            string[] lines = File.ReadAllLines(exclusionsFile);

            var newLines = lines
                .Select(line => UpdateExclusionsLine(exclusionsFile, line))
                .Where(line => line is not null);

            if (additionalLines is not null)
            {
                newLines = newLines.Concat(additionalLines);
            }

            string updatedFileName = updatedFileTag is null
                ? $"Updated{exclusionsFile}"
                : $"Updated{Path.GetFileNameWithoutExtension(exclusionsFile)}.{updatedFileTag}{Path.GetExtension(exclusionsFile)}";
            string actualFilePath = Path.Combine(Config.LogsDirectory, updatedFileName);
            File.WriteAllLines(actualFilePath, newLines!);
        }
    }

    private bool CheckAndRemoveIfExcluded(string filePath, string suffix = NullSuffix)
    {
        foreach (var pair in _fileToSuffixToExclusions)
        {
            string exclusionsFile = pair.Key;
            Dictionary<string, HashSet<string>> suffixToExclusions = pair.Value;
            if (suffixToExclusions.TryGetValue(suffix, out HashSet<string>? exclusions))
            {
                foreach (string exclusion in exclusions)
                {
                    Matcher matcher = new();
                    matcher.AddInclude(exclusion);
                    if (matcher.Match(filePath).HasMatches)
                    {
                        RemoveUsedExclusion(exclusionsFile, exclusion, suffix);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // Returns a dictionary mapping file -> suffix -> exclusions
    private void ParseExclusionsFile(string exclusionsFile)
    {
        if (!Path.IsPathFullyQualified(exclusionsFile))
        {
            exclusionsFile = BaselineHelper.GetBaselineFilePath(exclusionsFile, _baselineSubDir);
        }

        if (!File.Exists(exclusionsFile))
        {
            return;
        }

        if (!_fileToSuffixToExclusions.ContainsKey(exclusionsFile))
        {
            _fileToSuffixToExclusions[exclusionsFile] = new Dictionary<string, HashSet<string>>();
        }

        foreach (var line in File.ReadLines(exclusionsFile))
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
            {
                continue;
            }

            if (trimmedLine.StartsWith("import:"))
            {
                var importFile = trimmedLine.Substring("import:".Length).Trim();

                if (_fileToSuffixToExclusions.ContainsKey(importFile))
                {
                    continue;
                }

                ParseExclusionsFile(importFile);
            }
            else
            {
                ParseExclusionLine(exclusionsFile, trimmedLine);
            }
        }
    }

    private void ParseExclusionLine(string exclusionsFile, string line)
    {
        string[] splitLine = line.Split("#")[0].Split("|");
        string exclusion = splitLine[0].Trim();

        if(_exclusionRegex is not null && !_exclusionRegex.IsMatch(exclusion))
        {
            // Exclusion does not match the exclusion regex, so we can skip it
            return;
        }

        IEnumerable<string> suffixes = splitLine.Length == 1
            ? new[] { NullSuffix }
            : splitLine[1].Split(',').Select(suffix => suffix.Trim());

        foreach (string suffix in suffixes)
        {
            if (!_fileToSuffixToExclusions[exclusionsFile].ContainsKey(suffix))
            {
                _fileToSuffixToExclusions[exclusionsFile][suffix] = new HashSet<string>();
            }

            _fileToSuffixToExclusions[exclusionsFile][suffix].Add(exclusion);
        }
    }

    private void RemoveUsedExclusion(string exclusionsFile, string exclusion, string suffix)
    {
        if (_fileToSuffixToUnusedExclusions.TryGetValue(exclusionsFile, out Dictionary<string, HashSet<string>>? suffixToUnusedExclusions))
        {
            if (suffixToUnusedExclusions.TryGetValue(suffix, out HashSet<string>? exclusions))
            {
                exclusions.Remove(exclusion);
            }
        }
    }

    private string? UpdateExclusionsLine(string exclusionsFile, string line)
    {
        string[] parts = line.Split('|');
        string exclusion = parts[0];
        var unusedSuffixes = _fileToSuffixToUnusedExclusions[exclusionsFile].Where(pair => pair.Value.Contains(exclusion)).Select(pair => pair.Key).ToList();

        if (!unusedSuffixes.Any())
        {
            // Exclusion is used in all suffixes, so we can keep it as is
            return line;
        }

        if (parts.Length == 1)
        {
            if (unusedSuffixes.Contains(NullSuffix))
            {
                // Exclusion is unused in the default suffix, so we can remove it entirely
                return null;
            }
            // Line is duplicated for other suffixes, but null suffix is used so we can keep it as is
            return line;
        }

        string suffixString = parts[1].Split('#')[0];
        var originalSuffixes = suffixString.Split(',').Select(suffix => suffix.Trim()).ToList();
        var newSuffixes = originalSuffixes.Except(unusedSuffixes).ToList();

        if (newSuffixes.Count == 0)
        {
            // All suffixes were unused, so we can remove the line entirely
            return null;
        }

        return line.Replace(suffixString, string.Join(",", newSuffixes));
    }
}
