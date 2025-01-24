// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class Exclusion : IEquatable<Exclusion>
{
    public string Pattern { get; init; }
    public HashSet<string?> Suffixes { get; init; }

    public Exclusion(string line)
    {
        string parsedLine = line.Split('#')[0].Trim();
        string[] splitLine = parsedLine.Split('|', 2); // Split on the first occurrence of '|'
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

    public override bool Equals(object? obj) => obj is Exclusion exclusion && Equals(exclusion);

    public bool Equals(Exclusion? other) =>
        other is not null && Pattern == other.Pattern && Suffixes.SetEquals(other.Suffixes);

    public override int GetHashCode() => HashCode.Combine(Pattern, Suffixes);

    /// <summary>
    /// Checks if the exclusion matches the path and suffix.
    /// <param name="path">The path to check.</param>
    /// <param name="suffix">The suffix to check.</param>
    /// <param name="pattern">The pattern that matched.</param>
    /// </summary>
    public bool HasMatch(string path, string? suffix)
    {
        if (Suffixes.Contains(suffix))
        {
            Matcher matcher = new(StringComparison.Ordinal);
            matcher.AddInclude(Pattern);
            if (matcher.Match(path).HasMatches)
            {
                return true;
            }
        }

        return false;
    }
}
