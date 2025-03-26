// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Build.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.DotNet.UnifiedBuild.Tasks;

public class SigningValidation : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Directory where the blobs and packages were downloaded to
    /// </summary>
    [Required]
    public required string ArtifactDownloadDirectory { get; init; }

    /// <summary>
    /// Path to the dotnet root directory
    /// </summary>
    [Required]
    public required string DotNetRootDirectory { get; init; }

    /// <summary>
    /// Paths to the Merged Manifest
    /// </summary>
    [Required]
    public required string MergedManifest { get; init; }

    /// <summary>
    /// Path to the output logs directory
    /// </summary>
    [Required]
    public required string OutputLogsDirectory { get; init; }

    private readonly string _signCheckFilesDirectory = Path.Combine(Path.GetTempPath(), "SignCheckFiles");
    private const int _signCheckTimeout = 60 * 60 * 1000; // 1 hour

    public override bool Execute()
    {
        try
        {
            PrepareFilesToSignCheck();

            RunSignCheck();

            ProcessSignCheckResults();

            Log.LogMessage(MessageImportance.High, "Signing validation completed.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Signing validation failed: {ex.Message}");
        }
        finally
        {
            // Clean up the sign check files directory
            if (Directory.Exists(_signCheckFilesDirectory))
            {
                Directory.Delete(_signCheckFilesDirectory, true);
            }
        }

        return !Log.HasLoggedErrors;
    }

    /// <summary>
    /// Gets the list of files to sign check from the merged manifest
    /// and copies them to the sign check directory.
    /// </summary>
    private void PrepareFilesToSignCheck()
    {
        Log.LogMessage(MessageImportance.High, "Preparing files to sign check...");

        IEnumerable<string> blobsToSignCheck = Enumerable.Empty<string>();
        IEnumerable<string> packagesToSignCheck = Enumerable.Empty<string>();

        using (Stream xmlStream = File.OpenRead(MergedManifest))
        {
            XDocument doc = XDocument.Load(xmlStream);

            // Extract blobs
            blobsToSignCheck = doc.Descendants("Blob")
                .Where(file => IsReleaseShipping(file))
                .Select(file =>
                    {
                        string id = ExtractAttribute(file, "Id");
                        string filename = Path.GetFileName(id);
                        return !string.IsNullOrEmpty(filename) ? filename : string.Empty;
                    })
                .Where(blob => !string.IsNullOrEmpty(blob));

            // Extract packages
            packagesToSignCheck = doc.Descendants("Package")
                .Where(file => IsReleaseShipping(file))
                .Select(file =>
                    {
                        string id = ExtractAttribute(file, "Id");
                        string version = ExtractAttribute(file, "Version");

                        return !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version)
                            ? $"{id}.{version}.nupkg"
                            : string.Empty;
                    })
                .Where(pkg => !string.IsNullOrEmpty(pkg));
        }

        ForceDirectory(_signCheckFilesDirectory);

        // Copy the shipping blobs and packages from the download directory to the signcheck directory
        foreach (string file in blobsToSignCheck.Concat(packagesToSignCheck))
        {
            string? sourcePath = Directory.GetFiles(ArtifactDownloadDirectory, file, SearchOption.AllDirectories).FirstOrDefault();
            string destinationPath = Path.Combine(_signCheckFilesDirectory, file);

            if (!string.IsNullOrEmpty(sourcePath))
            {
                if (File.Exists(destinationPath))
                {
                    Log.LogWarning($"File {file} already exists in {_signCheckFilesDirectory}, skipping copy.");
                }
                else
                {
                    File.Copy(sourcePath, destinationPath, true);
                }
            }
            else
            {
                Log.LogWarning($"File {file} not found in {ArtifactDownloadDirectory}");
            }
        }
    }

    /// <summary>
    /// Runs the signcheck task on the specified package base path
    /// </summary>
    private void RunSignCheck()
    {
        using (var process = new Process())
        {
            (string command, string arguments) = GetSignCheckCommandAndArguments();

            process.StartInfo = new ProcessStartInfo()
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // SignCheck writes console output to log files and we don't want to clutter the console output
            // so we set the output and error handlers to empty
            process.OutputDataReceived += (sender, args) => {  };
            process.ErrorDataReceived += (sender, args) => { };

            Log.LogMessage(MessageImportance.High, $"Running SignCheck on {_signCheckFilesDirectory}...");

            ForceDirectory(OutputLogsDirectory);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit(_signCheckTimeout);

            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
                throw new TimeoutException($"SignCheck on '{_signCheckFilesDirectory}' timed out after {_signCheckTimeout / 1000} seconds.");
            }
            
            if (process.ExitCode != 0)
            {
                throw new Exception($"SignCheck on '{_signCheckFilesDirectory}' failed with exit code {process.ExitCode}. " +
                    $"Check the log files for more details.");
            }

            Log.LogMessage(MessageImportance.High, $"SignCheck on '{_signCheckFilesDirectory}' completed successfully");
        }
    }

    private void ProcessSignCheckResults()
    {
        string resultsXml = GetLogPath("signcheck.xml");
        if (!File.Exists(resultsXml))
        {
            throw new FileNotFoundException($"SignCheck results XML file not found: {resultsXml}");
        }

        IEnumerable<string> unsignedResults = XDocument.Load(resultsXml).Descendants("File")
            .Where(file => ExtractAttribute(file, "Outcome") == "Unsigned")
            .Select(file => ExtractAttribute(file, "Name"))
            .Where(name => !string.IsNullOrEmpty(name));

        if (unsignedResults.Any())
        {
            Log.LogWarning($"There are {unsignedResults.Count()} unsigned files:");
            foreach (string result in unsignedResults)
            {
                Log.LogMessage(MessageImportance.High, $"   {result}");
            }

            throw new Exception($"SignCheck detected unsigned files.");
        }
    }

    /// <summary>
    /// Gets the command and arguments to run signcheck
    /// </summary>
    private (string command, string arguments) GetSignCheckCommandAndArguments()
    {
        // Log arguments
        string stdoutLogName = GetLogPath("signcheck.log");
        string stderrLogName = GetLogPath("signcheck.error.log");
        string xmlLogName = GetLogPath("signcheck.xml");

        // Other arguments
        string exclusionsFile = Path.Combine(DotNetRootDirectory, "eng", "SignCheckExclusionsFile.txt");
        string sdkTaskScript = Path.Combine(DotNetRootDirectory, "eng", "common", "sdk-task");

        // {0} is the script extension, {1} is the command prefix, {2} is additional args
        string argumentsTemplate =
            $"'{sdkTaskScript}.$scriptExtension$' " +
            $"$argumentPrefix$task SigningValidation " +
            $"$argumentPrefix$restore " +
            $"/p:PackageBasePath='{_signCheckFilesDirectory}' " +
            $"/p:SignCheckLog='{stdoutLogName}' " +
            $"/p:SignCheckErrorLog='{stderrLogName}' " +
            $"/p:SignCheckResultsXmlFile='{xmlLogName}' " +
            $"/p:SignCheckExclusionsFile='{exclusionsFile}' " +
            $"$additionalArgs$";

        string command = string.Empty;
        string arguments = string.Empty;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command = "powershell.exe";
            string formattedArguments = argumentsTemplate
                .Replace("$scriptExtension$", "ps1")
                .Replace("$argumentPrefix$", "-")
                .Replace("$additionalArgs$", "-msbuildEngine vs");
            arguments = $"& \"{formattedArguments}\"";
        }
        else
        {
            command = "/bin/bash";
            string formattedArguments = argumentsTemplate
                .Replace("$scriptExtension$", "sh")
                .Replace("$argumentPrefix$", "--")
                .Replace("$additionalArgs$", string.Empty);
            arguments = $"-c \"{formattedArguments}\"";
        }

        return (command, arguments);
    }

    /// <summary>
    /// Creates the directory if it does not exist
    /// </summary>
    /// <param name="directory">The directory to create</param>
    private void ForceDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Checks if the element has the "DotNetReleaseShipping" attribute set to "true".
    /// </summary>
    private static bool IsReleaseShipping(XElement element)
        => element.Attribute("DotNetReleaseShipping")?.Value == "true";

    /// <summary>
    /// Extracts the value of the specified attribute from the element and logs an error if it's missing or empty.
    /// </summary>
    private string ExtractAttribute(XElement element, string attributeName)
    {
        string? value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrEmpty(value))
        {
            Log.LogError($"{attributeName} is null or empty in element: {element}");
        }
        return value ?? string.Empty;
    }

    /// <summary>
    /// Gets the path to the log file
    /// </summary>
    /// <param name="logName">The name of the log file</param>
    private string GetLogPath(string logName) =>
        Path.Combine(OutputLogsDirectory, logName);
}
