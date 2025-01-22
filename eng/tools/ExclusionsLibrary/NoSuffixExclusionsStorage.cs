// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class NoSuffixExclusionsStorage : IExclusionsStorage
{
    /// <summary>
    /// _storage for exclusions.
    /// </summary>
    private Dictionary<string, HashSet<string>> _storage = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NoSuffixExclusionStorage"/> class.
    /// </summary>
    public NoSuffixExclusionsStorage() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoSuffixExclusionStorage"/> class.
    /// <param name="other">The exclusion storage to copy.</param>
    /// </summary>
    public NoSuffixExclusionsStorage(NoSuffixExclusionsStorage other)
    {
        foreach (var file in other._storage.Keys)
        {
            _storage[file] = new HashSet<string>(other._storage[file]);
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
    public IEnumerable<string> GetSuffixes(string file) => _storage[file];

    /// <summary>
    /// Adds an exclusion to the storage. Creates the necessary dictionaries and hash sets if they do not exist.
    /// <param name="file">The file to add the exclusion to.</param>
    /// <param name="exclusion">The exclusion to add.</param>
    /// </summary>
    public void Add(string file, string exclusion, IEnumerable<string>? _)
    {
        if (!_storage.ContainsKey(file))
        {
            _storage[file] = new HashSet<string>();
        }
        _storage[file].Add(exclusion);
    }

    /// <summary>
    /// Removes an exclusion from the storage. If the keys or values do not exist, nothing happens.
    /// <param name="file">The file to remove the exclusion from.</param>
    /// <param name="exclusion">The exclusion to remove.</param>
    /// </summary>
    public void Remove(string file, string exclusion, string? _)
    {
        if (_storage.ContainsKey(file))
        {
            _storage[file].Remove(exclusion);
        }
    }

    /// <summary>
    /// Checks if the storage contains a file and exclusion.
    /// <param name="file">The file to look for.</param>
    /// <param name="exclusion">The exclusion to look for. Optional if only looking for the file.</param>
    /// </summary>
    public bool Contains(string file, string? exclusion, string? _)
    {
        if (string.IsNullOrEmpty(exclusion))
        {
            return _storage.ContainsKey(file);
        }

        return _storage.ContainsKey(file) && _storage[file].Contains(exclusion);
    }

    /// <summary>
    /// Checks if a file path matches any of the exclusions in the storage.
    /// If a match is found, the file, and exclusion are returned.
    /// <param name="filePath">The file path to check.</param>
    /// <param name="match">The file and exclusion that matched.</param>
    /// </summary>
    public bool ContainsExclusionMatch(string filePath, string? _, out (string file, string exclusion, string? suffix) match)
    {
        foreach (string file in _storage.Keys)
        {
            foreach (string exclusion in _storage[file])
            {
                Matcher matcher = new();
                matcher.AddInclude(exclusion);
                if (matcher.Match(filePath).HasMatches)
                {
                    match = (file, exclusion, null);
                    return true;
                }
            }
        }

        match = (string.Empty, string.Empty, null);
        return false;
    }
}
