// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class ExclusionStorage
{
    /// <summary>
    /// Specifies whether we are using suffixes to narrow down the scope of exclusions.
    /// </summary>
    private bool _usingSuffixes;

    /// <summary>
    /// Storage for exclusions.
    /// If using suffixes, the storage is a dictionary of dictionaries of hash sets. Maps file -> suffix -> exclusions.
    //  If not using suffixes, the storage is a dictionary of hash sets. Maps file -> exclusions.
    /// </summary>
    private object _storage { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExclusionStorage"/> class.
    /// <param name="usingSuffixes">Optional flag to specify whether we are using suffixes to narrow down the scope of exclusions.</param>
    /// </summary>
    public ExclusionStorage(bool usingSuffixes)
    {
        _usingSuffixes = usingSuffixes;
        
        if (_usingSuffixes)
        {
            _storage = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        }
        else
        {
            _storage = new Dictionary<string, HashSet<string>>();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExclusionStorage"/> class.
    /// <param name="other">The exclusion storage to copy.</param>
    /// </summary>
    public ExclusionStorage(ExclusionStorage other)
    {
        _usingSuffixes = other._usingSuffixes;
        
        if (_usingSuffixes)
        {
            _storage = new Dictionary<string, Dictionary<string, HashSet<string>>>();
            foreach (var file in (Dictionary<string, Dictionary<string, HashSet<string>>>)other._storage)
            {
                var suffixes = new Dictionary<string, HashSet<string>>();
                foreach (var suffix in file.Value)
                {
                    suffixes[suffix.Key] = new HashSet<string>(suffix.Value);
                }
                ((Dictionary<string, Dictionary<string, HashSet<string>>>)_storage)[file.Key] = suffixes;
            }
        }
        else
        {
            _storage = new Dictionary<string, HashSet<string>>();
            foreach (var file in (Dictionary<string, HashSet<string>>)other._storage)
            {
                ((Dictionary<string, HashSet<string>>)_storage)[file.Key] = new HashSet<string>(file.Value);
            }
        }
    }

    /// <summary>
    /// Gets all the files in the storage.
    /// </summary>
    public IEnumerable<string> GetFiles()
    {
        if (_usingSuffixes)
        {
            return ((Dictionary<string, Dictionary<string, HashSet<string>>>)_storage).Keys;
        }
        else
        {
            return ((Dictionary<string, HashSet<string>>)_storage).Keys;
        }
    }

    /// <summary>
    /// Gets all the suffixes in the storage.
    /// <param name="file">The file to get the suffixes for.</param>
    /// </summary>
    public IEnumerable<string> GetSuffixes(string file)
    {
        if (!_usingSuffixes)
        {
            throw new InvalidOperationException("Suffixes are not being used.");
        }
        
        var storage = (Dictionary<string, Dictionary<string, HashSet<string>>>)_storage;
        return storage[file].Keys;
    }

    /// <summary>
    /// Adds an exclusion to the storage. Creates the necessary dictionaries and hash sets if they do not exist.
    /// <param name="file">The file to add the exclusion to.</param>
    /// <param name="suffix">The suffix to add the exclusion to. Optional if not using suffixes.</param>
    /// <param name="exclusion">The exclusion to add.</param>
    /// </summary>
    public void Add(string file, string? suffix, string exclusion)
    {
        if (_usingSuffixes)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                throw new ArgumentNullException(nameof(suffix));
            }

            var storage = (Dictionary<string, Dictionary<string, HashSet<string>>>)_storage;
            if (!storage.ContainsKey(file))
            {
                storage[file] = new Dictionary<string, HashSet<string>>();
            }

            if (!storage[file].ContainsKey(suffix))
            {
                storage[file][suffix] = new HashSet<string>();
            }

            storage[file][suffix].Add(exclusion);
        }
        else
        {
            var storage = (Dictionary<string, HashSet<string>>)_storage;
            if (!storage.ContainsKey(file))
            {
                storage[file] = new HashSet<string>();
            }

            storage[file].Add(exclusion);
        }
    }

    /// <summary>
    /// Removes an exclusion from the storage. If the keys or values do not exist, nothing happens.
    /// <param name="file">The file to remove the exclusion from.</param>
    /// <param name="suffix">The suffix to remove the exclusion from. Optional if not using suffixes.</param>
    /// <param name="exclusion">The exclusion to remove.</param>
    /// </summary>
    public void Remove(string file, string? suffix, string exclusion)
    {
        if (_usingSuffixes)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                throw new ArgumentNullException(nameof(suffix));
            }

            var storage = (Dictionary<string, Dictionary<string, HashSet<string>>>)_storage;
            if (storage.ContainsKey(file) && storage[file].ContainsKey(suffix))
            {
                storage[file][suffix].Remove(exclusion);
            }
        }
        else
        {
            var storage = (Dictionary<string, HashSet<string>>)_storage;
            if (storage.ContainsKey(file))
            {
                storage[file].Remove(exclusion);
            }
        }
    }

    /// <summary>
    /// Checks if the storage contains a file, suffix (if using suffixes), and exclusion.
    /// <param name="file">The file to look for.</param>
    /// <param name="suffix">The suffix to look for. Optional if not using suffixes or only looking for the file.</param>
    /// <param name="exclusion">The exclusion to look for. Optional if only looking for the file or the file and suffix.</param>
    /// </summary>
    public bool Contains(string file, string? suffix, string? exclusion)
    {
        if (_usingSuffixes)
        {
            var storage = (Dictionary<string, Dictionary<string, HashSet<string>>>)_storage;

            if (string.IsNullOrEmpty(suffix))
            {
                return storage.ContainsKey(file);
            }
            
            if (string.IsNullOrEmpty(exclusion))
            {
                return storage.ContainsKey(file) && storage[file].ContainsKey(suffix);
            }

            return storage.ContainsKey(file) && storage[file].ContainsKey(suffix) && storage[file][suffix].Contains(exclusion);
        }
        else
        {
            var storage = (Dictionary<string, HashSet<string>>)_storage;
            
            if (string.IsNullOrEmpty(exclusion))
            {
                return storage.ContainsKey(file);
            }

            return storage.ContainsKey(file) && storage[file].Contains(exclusion);
        }
    }

    /// <summary>
    /// Checks if a file path matches any of the exclusions in the storage.
    /// If a match is found, the file, suffix (if using suffixes), and exclusion are returned.
    /// <param name="filePath">The file path to check.</param>
    /// <param name="suffix">The suffix to narrow down the scope of exclusions. Optional if not using suffixes.</param>
    /// <param name="match">The file, suffix, and exclusion that matched.</param>
    /// </summary>
    public bool ContainsExclusionMatch(string filePath, string? suffix, out (string file, string? suffix, string exclusion) match)
    {
        if (_usingSuffixes)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                throw new ArgumentNullException(nameof(suffix));
            }

            var storage = (Dictionary<string, Dictionary<string, HashSet<string>>>)_storage;
            foreach (string file in storage.Keys)
            {
                if (storage[file].ContainsKey(suffix))
                {
                    foreach (string exclusion in storage[file][suffix])
                    {
                        Matcher matcher = new();
                        matcher.AddInclude(exclusion);
                        if (matcher.Match(filePath).HasMatches)
                        {
                            match = (file, suffix, exclusion);
                            return true;
                        }
                    }
                }
            }
        }

        else
        {
            var storage = (Dictionary<string, HashSet<string>>)_storage;

            foreach (string file in storage.Keys)
            {
                foreach (string exclusion in storage[file])
                {
                    Matcher matcher = new();
                    matcher.AddInclude(exclusion);
                    if (matcher.Match(filePath).HasMatches)
                    {
                        match = (file, null, exclusion);
                        return true;
                    }
                }
            }
        }

        match = (string.Empty, null, string.Empty);
        return false;
    }
}