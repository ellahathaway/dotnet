// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace ExclusionsLibrary;

internal class ExclusionsStorage
{
    IExclusionsStorage _storage { get; set; }

    public ExclusionsStorage(bool usingSuffixes)
    {
        if (usingSuffixes)
        {
            _storage = new SuffixExclusionsStorage();
        }
        else
        {
            _storage = new NoSuffixExclusionsStorage();
        }
    }

    public ExclusionsStorage(ExclusionsStorage other)
    {
        _storage = other._storage switch
        {
            SuffixExclusionsStorage s => new SuffixExclusionsStorage(s),
            NoSuffixExclusionsStorage n => new NoSuffixExclusionsStorage(n),
            _ => throw new InvalidOperationException("Unknown storage type.")
        };
    }

    public IEnumerable<string> GetFiles() => _storage.GetFiles();

    public IEnumerable<string> GetSuffixes(string file) => _storage.GetSuffixes(file);

    public void Add(string file, string exclusion, IEnumerable<string>? suffixes) => _storage.Add(file, exclusion, suffixes);

    public void Remove(string file, string exclusion, string? suffix) => _storage.Remove(file, exclusion, suffix);

    public bool Contains(string file, string? exclusion, string? suffix) => _storage.Contains(file, exclusion, suffix);

    public bool ContainsExclusionMatch(string filePath, string? suffix, out (string file, string exclusion, string? suffix) match) =>
        _storage.ContainsExclusionMatch(filePath, suffix, out match);
}
