// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class Exclusion : IEquatable<Exclusion>
{
    private string Pattern { get; }
    private HashSet<string?> Suffixes { get; }

    public Exclusion(string pattern, HashSet<string?> suffixes)
    {
        Pattern = pattern;
        Suffixes = suffixes;
    }

    public Exclusion(string pattern, string? suffix)
    {
        Pattern = pattern;
        Suffixes = new HashSet<string?> { suffix };
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

    public Exclusion(Exclusion other)
    {
        Pattern = other.Pattern;
        Suffixes = new HashSet<string?>(other.Suffixes);
    }

    public override bool Equals(Exclusion other)
    {
        return Pattern == other.Pattern && Suffixes.SetEquals(other.Suffixes);
    }

    public HashSet<string> GetSuffixes() => Suffixes;

    public string GetPattern() => Pattern;

    public bool HasMatch(string path, string? suffix, out string? pattern)
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

        pattern = null;
        return false;
    }
}