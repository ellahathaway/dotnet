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

public class ProtectedFilesValidation : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Path to the dotnet root directory
    /// </summary>
    [Required]
    public required string DotNetRootDirectory { get; init; }

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
        string path = Path.Combine("eng", "common");
        string pathPattern = @$"^{path}{Regex.Escape(Path.DirectorySeparatorChar.ToString())}.*$";
        VerifyFileNotChanged(pathPattern, path);
    }

    private void VerifyEngVersionDetails()
    {
        string path = Path.Combine("eng", "Version.Details.xml");
        string pathPattern = @$"^{path}$";
        VerifyFileNotChanged(pathPattern, path);
    }

    private void VerifyEngVersionsProps()
    {
        string path = Path.Combine("eng", "Versions.props");
        string pathPattern = @$"^{path}$";
        VerifyFileNotChanged(pathPattern, path);
    }

    private void VerifyFileNotChanged(string pathPattern, string pathName)
    {
        if (!isIgnorablePullRequestTitle())
        {
            string diffOutput = ExecuteProcess("git", $"diff --name-only {PullRequestTargetBranch}...", DotNetRootDirectory);
            string[] changedFiles = diffOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            Regex pathRegex = new Regex(pathPattern, RegexOptions.IgnoreCase);

            if (changedFiles.Any(file => pathRegex.IsMatch(file)))
            {
                throw new Exception($"'{pathName}' has been modified in this PR. Please make sure this is a rebootstrap or Source-Build release PR.");
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
                throw new TimeoutException($"{command} timed out after {Timeout / 1000} seconds.");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"{command} failed with exit code {process.ExitCode}.");
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
