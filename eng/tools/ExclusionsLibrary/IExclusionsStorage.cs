// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace ExclusionsLibrary;

internal interface IExclusionsStorage
{
    /// <summary>
    /// Gets all the files in the storage.
    /// </summary>
    public IEnumerable<string> GetFiles();

    /// <summary>
    /// Gets all the suffixes in the storage.
    /// <param name="file">The file to get the suffixes for.</param>
    /// </summary>
    public IEnumerable<string> GetSuffixes(string file) => throw new System.NotImplementedException();

    /// <summary>
    /// Adds an exclusion to the storage. Creates the necessary dictionaries and hash sets if they do not exist.
    /// <param name="file">The file to add the exclusion to.</param>
    /// <param name="exclusion">The exclusion to add.</param>
    /// <param name="suffixes">The suffixes to add the exclusion to. Optional if not using suffixes.</param>
    /// </summary>
    public void Add(string file, string exclusion, IEnumerable<string>? suffixes);

    /// <summary>
    /// Removes an exclusion from the storage. If the keys or values do not exist, nothing happens.
    /// <param name="file">The file to remove the exclusion from.</param>
    /// <param name="exclusion">The exclusion to remove.</param>
    /// <param name="suffix">The suffix to remove the exclusion from. Optional if not using suffixes.</param>
    /// </summary>
    public void Remove(string file, string exclusion, string? suffix);

    /// <summary>
    /// Checks if the storage contains a file, suffix (if using suffixes), and exclusion.
    /// <param name="file">The file to look for.</param>
    /// <param name="exclusion">The exclusion to look for. Optional if only looking for the file or the file and suffix.</param>
    /// <param name="suffix">The suffix to look for. Optional if not using suffixes or only looking for the file.</param>
    /// </summary>
    public bool Contains(string file, string? exclusion, string? suffix);

    /// <summary>
    /// Checks if a file path matches any of the exclusions in the storage.
    /// If a match is found, the file, suffix (if using suffixes), and exclusion are returned.
    /// <param name="filePath">The file path to check.</param>
    /// <param name="suffix">The suffix to narrow down the scope of exclusions. Optional if not using suffixes.</param>
    /// <param name="match">The file, exclusion, and suffix that matched.</param>
    /// </summary>
    public bool ContainsExclusionMatch(string filePath, string? suffix, out (string file, string exclusion, string? suffix) match);
}
