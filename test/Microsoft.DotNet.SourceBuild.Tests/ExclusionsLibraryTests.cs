// // Licensed to the .NET Foundation under one or more agreements.
// // The .NET Foundation licenses this file to you under the MIT license.
// // See the LICENSE file in the project root for more information.

// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using System.Text.Json;
// using System.Text.Json.Serialization;
// using System.Text.RegularExpressions;
// using Xunit;
// using Xunit.Abstractions;
// using ExclusionsLibrary;

// namespace Microsoft.DotNet.SourceBuild.Tests;

// /// <summary>
// /// 
// /// </summary>
// /// <remarks>
// /// 
// /// </remarks>
// // [Trait("Category", "ExclusionsLibraryTests")]
// public class ExclusionsLibraryTests
// {
//     public ExclusionsLibraryTests() { }

//     [Fact]
//     public void TestExclusionConstructor()
//     {
//         // Basic constructor
//         var exclusion = new Exclusion("path/to/file", new HashSet<string?> { "suffix1", "suffix2" });
//         Assert.NotNull(exclusion);
//         Assert.Equal("path/to/file", exclusion.Pattern);
//         Assert.Contains("suffix1", exclusion.Suffixes);
//         Assert.Contains("suffix2", exclusion.Suffixes);

//         // Line constructor - suffixes
//         exclusion = new Exclusion("path/to/file|suffix1,suffix2");
//         Assert.NotNull(exclusion);
//         Assert.Equal("path/to/file", exclusion.Pattern);
//         Assert.Contains("suffix1", exclusion.Suffixes);
//         Assert.Contains("suffix2", exclusion.Suffixes);

//         // Line constructor - no suffixes
//         exclusion = new Exclusion("path/to/file # some comment");
//         Assert.NotNull(exclusion);
//         Assert.Equal("path/to/file", exclusion.Pattern);
//         Assert.Contains(null, exclusion.Suffixes);
//         Assert.True(exclusion.Suffixes.Count == 1);

//         // Copy constructor
//         var exclusion2 = new Exclusion(exclusion);
//         Assert.NotNull(exclusion2);
//         Assert.Equal(exclusion.Pattern, exclusion2.Pattern);
//         exclusion2.Suffixes.Add("suffix3");
//         Assert.DoesNotContain("suffix3", exclusion.Suffixes);
//     }

//     [Fact]
//     public void TestExclusionsHelperNoSuffix()
//     {
//         // Basic exclusions
//         string[] exclusions = new string[]
//         {
//             "path/to/file1",
//             "path/to/file2"
//         };

//         string exclusionsFile = CreateExclusionsFile(exclusions);

//         ExclusionsHelper helper = new(exclusionsFile);

//         Assert.True(helper.IsFileExcluded("path/to/file1"));
//         Assert.False(helper.IsFileExcluded("path/to/file3"));

//         // Update baseline
//         helper.GenerateNewBaselineFile();
//         IEnumerable<string> updatedFile = File.ReadLines("Updated" + exclusionsFile);
//         Assert.Contains("path/to/file1", updatedFile);
//         Assert.True(updatedFile.Count() == 100);
//     }

//     private static string CreateExclusionsFile(string[] exclusions)
//     {
//         string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
//         File.WriteAllLines(path, exclusions);
//         return path;
//     }


//     // Create sample data for the tests. Things to test:
//     // - ExclusionsHelper functionality
//        // - basic
//        // - with import
//        // - with exclusion regex
//        // - update baseline
//          // - remove entire exclusion
//          // - remove suffix from exclusion
// }
