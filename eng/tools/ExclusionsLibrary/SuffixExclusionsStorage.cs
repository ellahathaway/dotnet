// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class SuffixExclusionsStorage : IExclusionsStorage
{
    /// <summary>
    /// Storage for exclusions.
    /// </summary>
    private Dictionary<string, Dictionary<string, HashSet<string>>> _storage = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SuffixExclusionsStorage"/> class.
    /// </summary>
    public SuffixExclusionsStorage() {}

    /// <summary>
    /// Initializes a new instance of the <see cref="SuffixExclusionsStorage"/> class.
    /// <param name="other">The exclusion _storage to copy.</param>
    /// </summary>
    public SuffixExclusionsStorage(SuffixExclusionsStorage other)
    {
        foreach (string file in other._storage.Keys)
        {
            _storage[file] = new Dictionary<string, HashSet<string>>();
            foreach (string suffix in other._storage[file].Keys)
            {
                _storage[file][suffix] = new HashSet<string>(other._storage[file][suffix]);
            }
        }
    }

    /// <summary>
    /// Gets all the files in the storage.
    /// </summary>
    public IEnumerable<string> GetFiles() => _storage.Keys;

    /// <summary>
    /// Gets all the suffixes in the storage.
    /// <param name="file">The file to get the suffixes for.</param>
    /// </summary>
    public IEnumerable<string> GetSuffixes(string file) => _storage[file].Keys;

    /// <summary>
    /// Adds an exclusion to the storage. Creates the necessary dictionaries and hash sets if they do not exist.
    /// <param name="file">The file to add the exclusion to.</param>
    /// <param name="exclusion">The exclusion to add.</param>
    /// <param name="suffix">The suffix to add the exclusion to.</param>
    /// </summary>
    public void Add(string file, string exclusion, IEnumerable<string>? suffixes)
    {
        if (suffixes is null)
        {
            throw new ArgumentNullException(nameof(suffixes));
        }

        if (!_storage.ContainsKey(file))
        {
            _storage[file] = new Dictionary<string, HashSet<string>>();
        }

        foreach (string suffix in suffixes)
        {
            if (!_storage[file].ContainsKey(suffix))
            {
                _storage[file][suffix] = new HashSet<string>();
            }

            _storage[file][suffix].Add(exclusion);
        }
    }

    /// <summary>
    /// Removes an exclusion from the storage. If the keys or values do not exist, nothing happens.
    /// <param name="file">The file to remove the exclusion from.</param>
    /// <param name="exclusion">The exclusion to remove.</param>
    /// <param name="suffix">The suffix to remove the exclusion from.</param>
    /// </summary>
    public void Remove(string file, string exclusion, string? suffix)
    {
        if (string.IsNullOrEmpty(suffix))
        {
            throw new ArgumentException("Suffix must not be null or empty.");
        }

        if (_storage.ContainsKey(file) && _storage[file].ContainsKey(suffix))
        {
            _storage[file][suffix].Remove(exclusion);
        }
    }

    /// <summary>
    /// Checks if the storage contains a file, suffix (if using suffixes), and exclusion.
    /// <param name="file">The file to look for.</param>
    /// <param name="exclusion">The exclusion to look for. Optional if only looking for the file or the file and suffix.</param>
    /// <param name="suffix">The suffix to look for. Optional if only looking for the file.</param>
    /// </summary>
    public bool Contains(string file, string? exclusion, string? suffix)
    {
        if (string.IsNullOrEmpty(suffix))
        {
            return _storage.ContainsKey(file);
        }
        
        if (string.IsNullOrEmpty(exclusion))
        {
            return _storage.ContainsKey(file) && _storage[file].ContainsKey(suffix);
        }

        return _storage.ContainsKey(file) && _storage[file].ContainsKey(suffix) && _storage[file][suffix].Contains(exclusion);
    }

    /// <summary>
    /// Checks if a file path matches any of the exclusions in the storage.
    /// If a match is found, the file, suffix (if using suffixes), and exclusion are returned.
    /// <param name="filePath">The file path to check.</param>
    /// <param name="match">The file, suffix, and exclusion that matched.</param>
    /// <param name="suffix">The suffix to narrow down the scope of exclusions. Optional if not using suffixes.</param>
    /// </summary>
    public bool ContainsExclusionMatch(string filePath, string? suffix, out (string file, string exclusion,string? suffix) match)
    {
        if (string.IsNullOrEmpty(suffix))
        {
            throw new ArgumentException("Suffix must not be null or empty.");
        }

        foreach (string file in _storage.Keys)
        {
            if (_storage[file].ContainsKey(suffix))
            {
                foreach (string exclusion in _storage[file][suffix])
                {
                    Matcher matcher = new();
                    matcher.AddInclude(exclusion);
                    if (matcher.Match(filePath).HasMatches)
                    {
                        match = (file, exclusion, suffix);
                        return true;
                    }
                }
            }
        }

        match = (string.Empty, string.Empty, null);
        return false;
    }

    private static void ValidateSuffix(string? suffix)
    {
        if (string.IsNullOrEmpty(suffix))
        {
            throw new ArgumentException("Suffix must not be null or empty.");
        }
    }
}
