// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Reflection;

[assembly:UnsupportedOSPlatform("windows")]

namespace Microsoft.DotNet.SourceBuild.Tasks.Sign;

internal static class Config
{
    private static string TaskAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException("Unable to determine task assembly location.");
    private static string StaticDirectory = Path.Combine(TaskAssemblyLocation, "static");
    private const string ConfigSwitchPrefix = "Microsoft.DotNet.SourceBuild.Tasks.Sign.";
    private const string AspNetCoreSnkFileName = "AspNetCore.snk";
    private const string EmptyProjectFileName = "empty.proj";
    public const string SigningPropsFileName = "Signing.props";
    public static string AspNetCoreSnkPath => Path.Combine(StaticDirectory, AspNetCoreSnkFileName);
    public static string EmptyProjectPath => Path.Combine(StaticDirectory, EmptyProjectFileName);
    public static string SigningPropsPath => Path.Combine(StaticDirectory, SigningPropsFileName);
}
