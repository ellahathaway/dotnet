// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class Exclusion : IEquatable<Exclusion>
{
    public string Pattern { get; init; }
    public HashSet<string?> Suffixes { get; init; }

    public Exclusion(string pattern, HashSet<string?> suffixes)
    {
        Pattern = pattern;
        Suffixes = suffixes;
    }

    public Exclusion(string line)
    {
        string parsedLine = line.Split('#')[0].Trim();
        string[] splitLine = parsedLine.Split('|');
        Pattern = splitLine[0].Trim();

        Suffixes = splitLine.Length > 1
            ? new HashSet<string?>(splitLine[1].Split(',').Select(s => s.Trim()))
            : new HashSet<string?> { null };
    }

    public Exclusion(Exclusion other) : this(other.Pattern, new HashSet<string?>(other.Suffixes)) { }

    public bool Equals(Exclusion? other) =>
        other is not null && Pattern == other.Pattern && Suffixes.SetEquals(other.Suffixes);

    /// <summary>
    /// Checks if the exclusion matches the path and suffix.
    /// <param name="path">The path to check.</param>
    /// <param name="suffix">The suffix to check.</param>
    /// <param name="pattern">The pattern that matched.</param>
    /// </summary>
    public bool HasMatch(string path, string? suffix, out string pattern)
    {
        if (Suffixes.Contains(suffix))
        {
            Matcher matcher = new();
            matcher.AddInclude(Pattern);
            if (matcher.Match(path).HasMatches)
            {
                pattern = Pattern;
                return true;
            }
        }

        pattern = string.Empty;
        return false;
    }
}