// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

[assembly:UnsupportedOSPlatform("windows")]

namespace Microsoft.DotNet.SourceBuild.Tasks.Sign;

public class SignArtifacts : Microsoft.Build.Utilities.Task
{

    /// <summary>
    /// Path to the eng directory
    /// </summary>
    [Required]
    public required string DotNetEngDirectory { get; init; }

    /// <summary>
    /// Path to the source-built SDK tarball
    /// </summary>
    [Required]
    public required string SourceBuiltSdkTarballPath { get; init; }

    /// <summary>
    /// Path to the source-built artifacts
    /// </summary>
    [Required]
    public required string SourceBuiltArtifactsPath { get; init; }

    /// <summary>
    /// The official build ID
    /// </summary>
    [Required]
    public required string OfficialBuildId { get; init; }

    /// <summary>
    /// Path to the logs directory
    /// </summary>
    [Required]
    public required string LogsDirectory { get; init; }

    /// <summary>
    /// Sign-specific properties to pass to the signing process
    /// </summary>
    public string SignProperties { get; init; } = string.Empty;

    private string SigningPropsTargetPath => Path.Combine(DotNetEngDirectory, Config.SigningPropsFileName);
    private const string BinlogFileName = "sign-source-built-artifacts.binlog";
    private const int SigningTimeout = 60 * 60 * 1000 * 2; // 2 hours

    public override bool Execute()
    {
        try
        {
            PrepareForSigning();

            RunSigning();

            Log.LogMessage(MessageImportance.High, "Signing complete.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Signing failed: {ex.Message}");
        }
        finally
        {
            if (File.Exists(SigningPropsTargetPath))
            {
                File.Delete(SigningPropsTargetPath);
            }
        }

        return !Log.HasLoggedErrors;
    }

    private void PrepareForSigning()
    {
        if (!File.Exists(SourceBuiltArtifactsPath))
        {
            throw new FileNotFoundException($"Source built artifacts not found at {SourceBuiltArtifactsPath}");
        }

        if (!File.Exists(SourceBuiltSdkTarballPath))
        {
            throw new FileNotFoundException($"Source built SDK tarball not found at {SourceBuiltSdkTarballPath}");
        }

        if (!Directory.Exists(LogsDirectory))
        {
            Directory.CreateDirectory(LogsDirectory);
        }

        // Place the Signing.props file into the repo's eng directory for Arcade build to consume
        File.Copy(Config.SigningPropsPath, SigningPropsTargetPath, overwrite: true);
    }

    /// <summary>
    /// Runs the signing
    /// </summary>
    private void RunSigning()
    {
        using (var process = new Process())
        {
            (string command, string arguments) = GetSigningCommandAndArguments();

            process.StartInfo = new ProcessStartInfo()
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                EnvironmentVariables = { ["OPENSSL_ENABLE_SHA1_SIGNATURES"] = "1" }
            };

            process.OutputDataReceived += (sender, args) => { Log.LogMessage(MessageImportance.High, args.Data); };
            process.ErrorDataReceived += (sender, args) => { Log.LogError(args.Data); };

            Log.LogMessage(MessageImportance.High, $"Running signing...");

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool hasExited = process.WaitForExit(SigningTimeout);
            if (!hasExited)
            {
                throw new TimeoutException($"Signing timed out after {SigningTimeout / 1000} seconds.");
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"Signing failed with exit code {process.ExitCode}");
            }

            Log.LogMessage(MessageImportance.High, $"Signing completed.");
        }
    }

    /// <summary>
    /// Gets the command and arguments to run signing
    /// </summary>
    private (string command, string arguments) GetSigningCommandAndArguments()
    {
        string buildScript = Path.Combine(DotNetEngDirectory, "common", "build.sh");
        string binlog = Path.Combine(LogsDirectory, BinlogFileName);

        // Build the argument string with proper quoting to avoid shell expansion issues
        string formattedArguments =
            $"'{buildScript}'" +
            $" --ci" +
            $" --configuration Release" +
            $" --restore" +
            $" --projects '{Config.EmptyProjectPath}'" +
            $" --sign" +
            $" --excludecibinarylog" +
            $" /bl:'{binlog}'" +
            $" {SignProperties}" +
            $" /p:OfficialBuildId={OfficialBuildId}" +
            $" /p:AspNetCoreSnkPath='{Config.AspNetCoreSnkPath}'" +
            $" /p:SourceBuiltArtifactsPath='{SourceBuiltArtifactsPath}'" +
            $" /p:SourceBuiltSdkTarballPath='{SourceBuiltSdkTarballPath}'";

        return ("/bin/bash", $"-c \"{formattedArguments}\"");
    }
}
