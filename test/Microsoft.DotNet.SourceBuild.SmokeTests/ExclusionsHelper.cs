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

namespace Microsoft.DotNet.SourceBuild.SmokeTests;

internal static class ExclusionsHelper
{
    private static Dictionary<string, Dictionary<string, HashSet<string>>> FileNamesToExclusions = new();

    private static Dictionary<string, Dictionary<string, HashSet<string>>> FileNamesToUnusedExclusions = new();
    
    private const string NullSuffix = "NULL_SUFFIX";

    // Set this if you want to only focus on a specific category of exclusions.
    // An example would be "test-templates" in order to include "src/test-templates/exclusions.txt|cs,csproj"
    public static string ExclusionCategory = null;

    static ExclusionsHelper()
    {
        AppDomain.CurrentDomain.ProcessExit += onProcessExit;
    }

    private static void onProcessExit(object? sender, EventArgs e)
    {
        RemoveUnusedExclusionsFromBaselines();
    }

    internal static bool IsFileExcluded(string filePath, string exclusionsFileName, string suffix = NullSuffix)
    {
        // If a specific suffix is provided, check that first. If it is not found, check the default suffix.
        return CheckAndRemoveIfExcluded(filePath, exclusionsFileName, suffix) ||
            (suffix != NullSuffix && CheckAndRemoveIfExcluded(filePath, exclusionsFileName, NullSuffix));
    }

    private static bool CheckAndRemoveIfExcluded(string filePath, string exclusionsFileName, string suffix = NullSuffix)
    {
        Dictionary<string, HashSet<string>> exclusions = GetExclusions(exclusionsFileName);

        if (exclusions.TryGetValue(suffix, out HashSet<string> suffixExclusionList))
        {
            foreach (string exclusion in suffixExclusionList)
            {
                Matcher matcher = new();
                matcher.AddInclude(exclusion);
                if (matcher.Match(filePath).HasMatches)
                {
                    RemoveUsedExclusion(exclusionsFileName, exclusion, NullSuffix);
                    return true;
                }
            }
        }
        return false;
    }

    internal static Dictionary<string, HashSet<string>> GetExclusions(string exclusionsFileName)
    {
        if (!FileNamesToExclusions.TryGetValue(exclusionsFileName, out Dictionary<string, HashSet<string>> exclusions))
        {
            exclusions = ParseExclusionsFile(exclusionsFileName);
            FileNamesToExclusions[exclusionsFileName] = exclusions;
            FileNamesToUnusedExclusions[exclusionsFileName] = new Dictionary<string, HashSet<string>>(
                exclusions.ToDictionary(pair => pair.Key, pair => new HashSet<string>(pair.Value)));
        }

        return exclusions;
    }

    private static Dictionary<string, HashSet<string>> ParseExclusionsFile(string exclusionsFileName)
    {
        string exclusionsFilePath = Path.Combine(BaselineHelper.GetAssetsDirectory(), exclusionsFileName);
        return File.ReadAllLines(exclusionsFilePath)
            .Select(line =>
            {
                // Ignore comments
                var index = line.IndexOf('#');
                return index >= 0 ? line[..index].TrimEnd() : line;
            })
            .Where(line => !string.IsNullOrEmpty(line))
            .Select(line => line.Split('|'))
            .Where(parts => ExclusionCategory is null || parts[0].Contains(ExclusionCategory))
            .Select(parts => new {
                Line = parts[0],
                Suffixes = parts.Length > 1 ? parts[1].Split(',').Select(suffix => suffix.Trim()) : new string[] { NullSuffix } })
            .SelectMany(parts => parts.Suffixes.Select(suffix => new { parts.Line, Suffix = suffix }))
            .GroupBy(parts => parts.Suffix, parts => parts.Line)
            .ToDictionary(group => group.Key, group => new HashSet<string>(group));
    }

    private static void RemoveUsedExclusion(string exclusionsFileName, string exclusion, string suffix)
    {
        if (FileNamesToUnusedExclusions.TryGetValue(exclusionsFileName, out Dictionary<string, HashSet<string>> exclusions))
        {
            if (exclusions.TryGetValue(suffix, out HashSet<string> exclusionList))
            {
                exclusionList.Remove(exclusion);
            }
        }
    }

    private static void RemoveUnusedExclusionsFromBaselines()
    {

        foreach (KeyValuePair<string, Dictionary<string, HashSet<string>>> fileToUnusedExclusions in FileNamesToUnusedExclusions)
        {
            string exclusionsFileName = fileToUnusedExclusions.Key;
            string exclusionsFilePath = Path.Combine(BaselineHelper.GetAssetsDirectory(), exclusionsFileName);

            string[] lines = File.ReadAllLines(exclusionsFilePath);
            
            // Remove unused exclusions from the file
            foreach (var suffixExclusionList in fileToUnusedExclusions.Value)
            {
                string suffix = suffixExclusionList.Key;
                if (suffix == NullSuffix)
                {
                    // Remove all unused exclusions
                    lines = lines.Where(line => !suffixExclusionList.Value.Contains(line)).ToArray();
                }
                else
                {
                    // Remove only the specific suffix, unless it is the only suffix
                    foreach (string exclusion in suffixExclusionList.Value)
                    {
                        lines = lines.Select(line =>
                        {
                            if (line.Contains(exclusion) && line.Contains(suffix))
                            {
                                if (line.Contains($",{suffix}"))
                                {
                                    return line.Replace($",{suffix}", string.Empty);
                                }
                                else if (line.Contains($"{suffix},"))
                                {
                                    return line.Replace($"{suffix},", string.Empty);
                                }
                                else if (line.Contains(suffix))
                                {
                                    return string.Empty;
                                }
                            }
                            return line;
                        }).ToArray();
                    }
                }
            }
            
            string actualFilePath = Path.Combine(TestBase.LogsDirectory, $"Updated-{ExclusionCategory}-{exclusionsFileName}");
            File.WriteAllLines(actualFilePath, lines);
        }
    }
}