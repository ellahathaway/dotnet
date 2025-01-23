// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class ExclusionsStorage
{
    private Dictionary<string, List<Exclusion>> _storage = new();

    public ExclusionsStorage(ExclusionsStorage other)
    {
        foreach (string file in other._storage.Keys)
        {
            _storage[file] = other._storage[file].Select(e => new Exclusion(e)).ToList();
        }
    }

    public IEnumerable<string> GetFiles() => _storage.Keys;

    public IEnumerable<string> GetSuffixes(string file) => _storage[file].SelectMany(exclusion => exclusion.GetSuffixes());

    /// <summary>
    /// Adds an exclusion to the storage if it doesn't already exist.
    /// <param name="file">The file to add the exclusion to.</param>
    /// <param name="exclusion">The exclusion to add.</param>
    /// </summary>
    public void Add(string file, Exclusion exclusion)
    {
        if (!_storage.ContainsKey(file))
        {
            _storage[file] = new List<Exclusion>();
        }
        _storage[file].Add(exclusion);
    }

    /// <summary>
    /// Removes a suffix from an exclusion in the storage. If there are no suffixes left, the exclusion will be removed.
    /// <param name="file">The file for the exclusion.</param>
    /// <param name="pattern">The pattern to look for.</param>
    /// <param name="suffix">The suffix to remove.</param>
    /// </summary>
    public void Remove(string file, string pattern, string? suffix)
    {
        if (_storage.ContainsKey(file))
        {
            Exclusion? exclusion = _storage[file].FirstOrDefault(e => e.GetPattern() == pattern);
            if (exclusion is not null)
            {
                _storage[file].Remove(exclusion);
                exclusion.GetSuffixes().Remove(suffix);
                if (exclusion.GetSuffixes().Count > 0)
                {
                    _storage[file].Add(exclusion);
                }
            }
        }
    }

    public bool Contains(string file, Exclusion? exclusion = null) =>
        _storage.ContainsKey(file) && (exclusion is null || _storage[file].Contains(exclusion));

    public Exclusion? GetExclusion(string file, string pattern) =>
        _storage.ContainsKey(file) ? _storage[file].FirstOrDefault(e => e.GetPattern() == pattern) : null;

    public bool HasMatch(string filePath, string? suffix, out (string file, string pattern)? match)
    {
        foreach (string file in _storage.Keys)
        {
            foreach (Exclusion exclusion in _storage[file])
            {
                if (exclusion.HasMatch(filePath, suffix, out string? pattern))
                {
                    match = (file, pattern);
                    return true;
                }
            }
        }

        match = null;
        return false;
    }
}
