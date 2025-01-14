#!/usr/bin/env bash

### Usage: $0
###
###   Prepares and runs the VMR's tools.
###
### Options:
###   --tool <tool>              Name of VMR tool (name of a project in the eng/tools directory).
###   --list                     List of available tools to run.
###
### Advanced Options:
###   --with-packages            Use the specified directory as the packages source feed.
###                              Defaults to online dotnet-public and dotnet-libraries feeds.
###   --with-sdk                 Use the specified directory as the dotnet SDK.
###                              Defaults to .dotnet.
###   Command line arguments not listed above are passed thru to dotnet run.

set -euo pipefail
IFS=$'\n\t'

source="${BASH_SOURCE[0]}"
REPO_ROOT="$( cd -P "$( dirname "$0" )/../" && pwd )"
TOOL_ROOT="$REPO_ROOT/eng/tools/"

function print_help () {
    sed -n '/^### /,/^$/p' "$source" | cut -b 5-
}

defaultDotnetSdk="$REPO_ROOT/.dotnet"

# Set default values
tool=''
toolArgs=''
propsDir=''
packagesDir=''
restoreSources=''
dotnetSdk=$defaultDotnetSdk

if [ $# -eq 0 ]; then
    print_help
    exit 0
fi

positional_args=()
while :; do
  if [ $# -le 0 ]; then
    break
  fi
  lowerI="$(echo "$1" | awk '{print tolower($0)}')"
  case $lowerI in
    "-?"|-h|--help)
      print_help
      exit 0
      ;;
    --tool)
      tool=$(find "$TOOL_ROOT" -name "$2" -type d)
      if [ ! -d "$tool" ]; then
        echo "ERROR: The specified tool does not exist in '$TOOL_ROOT'."
        exit 1
      fi
      tool_project=$(find "$tool" -name "$2.csproj")
      if [ ! -f "$tool_project" ]; then
        echo "ERROR: The specified tool's project file does not exist in '$tool_project'."
        exit 1
      fi
      shift
      ;;
    --list)
        echo "Available tools:"
        find "$TOOL_ROOT" -name '*.csproj' | grep -v tasks | xargs -n 1 basename | sed 's/.csproj//' | sort | xargs -I {} echo "  {}"
        exit 0
        ;;
    --with-packages)
      packagesDir=$2
      if [ ! -d "$packagesDir" ]; then
        echo "ERROR: The specified packages directory does not exist."
        exit 1
      elif [ ! -f "$packagesDir/PackageVersions.props" ]; then
        echo "ERROR: The specified packages directory does not contain PackageVersions.props."
        exit 1
      fi
      shift
      ;;
    --with-sdk)
      dotnetSdk=$2
      if [ ! -d "$dotnetSdk" ]; then
        echo "Custom SDK directory '$dotnetSdk' does not exist"
        exit 1
      fi
      if [ ! -x "$dotnetSdk/dotnet" ]; then
        echo "Custom SDK '$dotnetSdk/dotnet' does not exist or is not executable"
        exit 1
      fi
      shift
      ;;
    *)
      # skip on empty strings
      if [ -n "$1" ]; then
        toolArgs="$toolArgs $1"
      fi
      positional_args+=("$1")
      ;;
  esac
  shift
done

function ValidateArgs
{
  # Check tool
  if [ -z "$tool" ]; then
    echo "ERROR: --tool is required. Use --list to see available tools."
    exit 1
  fi

  # Check dotnet sdk
  if [ "$dotnetSdk" == "$defaultDotnetSdk" ]; then
    if [ ! -d "$dotnetSdk" ]; then
      . "$REPO_ROOT/eng/common/tools.sh"
      InitializeDotNetCli true
    fi
    else if [ ! -x "$dotnetSdk/dotnet" ]; then
      echo "'$dotnetSdk/dotnet' does not exist or is not executable"
      exit 1
    fi
  fi

  # Check the packages directory
  if [ -z "$packagesDir" ]; then
    # Use dotnet-public and dotnet-libraries feeds as the default packages source feeds
    restoreSources="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json%3Bhttps://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-libraries/nuget/v3/index.json"
  else
    restoreSources=$(realpath ${packagesDir})
  fi
}

function RunTool
{
  targetDir="$REPO_ROOT"
  extraArgs=""
  if [ -n "$packagesDir" ]; then
    extraArgs="-p CustomPackageVersionsProps=\"$packagesDir/PackageVersions.props\""
  fi
  
  if [ -n "$toolArgs" ]; then
    extraArgs="$extraArgs $toolArgs"
  fi
  echo "$extraArgs"

  "$dotnetSdk/dotnet" run --project $tool -c Release --property:RestoreSources=\"$restoreSources\" $toolArgs
}

ValidateArgs
RunTool
