// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.DotNet.UnifiedBuild.Tasks;

public class VersionValidation : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Path to the dotnet root directory
    /// </summary>
    [Required]
    public required string DotNetRootDirectory { get; init; }

    /// <summary>
    /// Path to the darc executable
    /// </summary>
    [Required]
    public required string DarcPath { get; init; }

    /// <summary>
    /// Pull request target branch
    /// </summary>
    [Required]
    public required string PullRequestTargetBranch { get; init; }

    /// <summary>
    /// Pull request title
    /// </summary>
    [Required]
    public required string PullRequestTitle { get; init; }

    private static readonly string ArcadeSdkDependency = "Microsoft.DotNet.Arcade.Sdk";
    private const int Timeout = 60000; // 1 minute

    public override bool Execute()
    {
        RunTest(VerifyEngCommon, nameof(VerifyEngCommon));
        RunTest(VerifyEngVersionDetails, nameof(VerifyEngVersionDetails));
        RunTest(VerifyEngVersionsProps, nameof(VerifyEngVersionsProps));

        return !Log.HasLoggedErrors;
    }

    private void VerifyEngCommon()
    {
        string versionDetailsPath = Path.Combine(DotNetRootDirectory, "eng", "Version.Details.xml");
        if (!File.Exists(versionDetailsPath))
        {
            Log.LogError($"Version.Details.xml not found at {versionDetailsPath}");
        }

        XDocument versionDetails = XDocument.Load(versionDetailsPath);
        string arcadeVersion = versionDetails.Descendants("Dependency")
            .FirstOrDefault(d => (string?)d.Attribute("Name") == "Microsoft.DotNet.Arcade.Sdk")
            ?.Attribute("Version")?.Value ?? throw new Exception($"Arcade version not found in {versionDetailsPath}");

        ExecuteProcess(DarcPath, $"update-dependencies --source-repo dotnet --name {ArcadeSdkDependency} --version {arcadeVersion} --ci", DotNetRootDirectory);

        string diffOutput = ExecuteProcess("git", $"diff -- eng/common/", DotNetRootDirectory);
        if (!string.IsNullOrEmpty(diffOutput))
        {
            throw new Exception($"eng/common/ is not in sync with {ArcadeSdkDependency}.{arcadeVersion}.");
        }
    }

    private void VerifyEngVersionDetails()
       => VerifyFileNotChanged(Path.Combine("eng", "Version.Details.xml"));

    private void VerifyEngVersionsProps()
        => VerifyFileNotChanged(Path.Combine("eng", "Versions.props"));

    private void VerifyFileNotChanged(string filePath)
    {
        if (!isIgnorablePullRequestTitle())
        {
            string diffOutput = ExecuteProcess("git", $"diff --name-only {PullRequestTargetBranch}...", DotNetRootDirectory);
            if (diffOutput.Contains(filePath))
            {
                throw new Exception($"{filePath} has been modified in this PR. Please make sure this is a rebootstrap or Source-Build release PR.");
            }
        }
    }

    private void RunTest(Action testAction, string testName)
    {
        try
        {
            testAction();
            Log.LogMessage(MessageImportance.High, $"Test '{testName}' passed.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Test '{testName}' failed: {ex.Message}");
        }
    }

    private string ExecuteProcess(string command, string args, string? workingDirectory = null)
    {
        using (var process = new Process())
        {
            if (string.IsNullOrEmpty(workingDirectory))
            {
                workingDirectory = Directory.GetCurrentDirectory();
            }

            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
            };

            StringBuilder output = new StringBuilder();
            process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    output.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    Log.LogError(args.Data);
                }
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool hasExited = process.WaitForExit(Timeout);
            if (!hasExited)
            {
                process.Kill();
                throw new TimeoutException($"{command} {args} timed out after {Timeout / 1000} seconds.");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"{command} {args} failed with exit code {process.ExitCode}.");
            }

            return output.ToString();
        }
    }

    private bool isIgnorablePullRequestTitle()
    {
        string titlePattern = @"^(rebootstrap|re-bootstrap|release|\.NET Source-Build \* Updates)";
        return Regex.IsMatch(PullRequestTitle, titlePattern, RegexOptions.IgnoreCase);
    }
}
