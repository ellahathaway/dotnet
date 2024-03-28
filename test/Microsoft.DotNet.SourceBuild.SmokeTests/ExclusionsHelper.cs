// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// This is a static singleton class that provides helper methods for managing exclusions.
// if a new exclusions file is read, it will be cached in the static Exclusions dictionary.
// if should keep track of all unused exclusions and write them to a file at the end of the test run.

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

    static ExclusionsHelper()
    {
        AppDomain.CurrentDomain.ProcessExit += onProcessExit;
    }

    private static void onProcessExit(object? sender, EventArgs e)
    {
        RemoveUnusedExclusionsFromBaselines();
    }

    internal static bool IsFileExcluded(string filePath, string exclusionsFileName, string suffix = NullSuffix, ITestOutputHelper outputHelper = null)
    {
        Dictionary<string, HashSet<string>> exclusions = GetExclusions(exclusionsFileName, outputHelper);

        // check if the file is excluded by the suffix
        if (exclusions.TryGetValue(suffix, out HashSet<string> exclusionList))
        {
            foreach (string exclusion in exclusionList)
            {
                Matcher matcher = new();
                matcher.AddInclude(exclusion);
                if (matcher.Match(filePath).HasMatches)
                {
                    RemoveUsedExclusion(exclusionsFileName, exclusion, suffix);
                    return true;
                }
            }
        }

        // check if the file is excluded by the null suffix
        if (suffix != NullSuffix && exclusions.TryGetValue(NullSuffix, out HashSet<string> nullSuffixExclusionList))
        {
            foreach (string exclusion in nullSuffixExclusionList)
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

    internal static Dictionary<string, HashSet<string>> GetExclusions(string exclusionsFileName, ITestOutputHelper outputHelper = null)
    {
        if (!FileNamesToExclusions.TryGetValue(exclusionsFileName, out Dictionary<string, HashSet<string>> exclusions))
        {
            exclusions = ParseExclusionsFile(exclusionsFileName, outputHelper);
            FileNamesToExclusions[exclusionsFileName] = exclusions;
            FileNamesToUnusedExclusions[exclusionsFileName] = exclusions;
        }

        return exclusions;
    }

    private static Dictionary<string, HashSet<string>> ParseExclusionsFile(string exclusionsFileName, ITestOutputHelper outputHelper)
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
            .Select(parts => new { Line = parts[0], Suffixes = parts.Length > 1 ? parts[1].Split(',').Select(suffix => suffix.Trim()) : new string[] { NullSuffix } })
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

    internal static void RemoveUnusedExclusionsFromBaselines()
    {
        foreach (KeyValuePair<string, Dictionary<string, HashSet<string>>> fileToUnusedExclusions in FileNamesToUnusedExclusions)
        {
            string exclusionsFileName = fileToUnusedExclusions.Key;
            string exclusionsFilePath = Path.Combine(BaselineHelper.GetAssetsDirectory(), exclusionsFileName);
            string[] lines = File.ReadAllLines(exclusionsFilePath);
            foreach (KeyValuePair<string, HashSet<string>> unusedExclusions in fileToUnusedExclusions.Value)
            {
                string suffix = unusedExclusions.Key;
                foreach (string exclusion in unusedExclusions.Value)
                {
                    // grab the line from the exclusions file
                    string line = lines.FirstOrDefault(l => l.Contains(exclusion))!;
                    if (line != null)
                    {
                        // if the exclusion is just exclusion, remove the whole line.
                        if (line == exclusion)
                        {
                            lines = lines.Where(l => l != line).ToArray();
                        }
                        // if the exclusion is exclusion|suffix, remove the whole line.
                        else if (line == ($"{exclusion}|{suffix}"))
                        {
                            lines = lines.Where(l => l != line).ToArray();
                        }
                        // if the exclusion is exclusion|suffix,other, remove just the suffix and the comma if the comma exists.
                        else if (line.Contains(exclusion) && line.Contains(suffix))
                        {
                            if (line.Contains($"{suffix},"))
                            {
                                string newLine = line.Replace($"{suffix},", string.Empty);
                                lines = lines.Select(l => l == line ? newLine : l).ToArray();
                            }
                            else if (line.Contains($",{suffix}"))
                            {
                                string newLine = line.Replace($",{suffix}", string.Empty);
                                lines = lines.Select(l => l == line ? newLine : l).ToArray();
                            }
                            else
                            {
                                lines = lines.Where(l => l != line).ToArray();
                            }
                        }
                    }
                }
            }
            string actualFilePath = Path.Combine(TestBase.LogsDirectory, $"Updated{exclusionsFileName}");
            File.WriteAllLines(actualFilePath, lines);
        }
    }
}


public class ExclusionsHelperFixture : IDisposable
{
    public ExclusionsHelperFixture()
    {
        // Setup code here
    }

    public void Dispose()
    {
        // This code runs after all tests in the collection have finished
        ExclusionsHelper.RemoveUnusedExclusionsFromBaselines();
    }
}