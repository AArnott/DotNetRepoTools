# Nerdbank.DotNetRepoTools

***A CLI toolbox for repo maintenance***

[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.DotNetRepoTools.svg)](https://nuget.org/packages/Nerdbank.DotNetRepoTools)
[![Build Status](https://dev.azure.com/andrewarnott/OSS/_apis/build/status/DotNetRepoTools/DotNetRepoTools?branchName=main)](https://dev.azure.com/andrewarnott/OSS/_build/latest?definitionId=74&branchName=main)

## Usage

### Acquisition

Install or upgrade this tool with the following command:

    dotnet tool update -g Nerdbank.DotNetRepoTools --prerelease

## Commands

After install, use the tool name `repotools` to run commands.

This CLI tool has (or will have) a variety of commands and sub-commands, discoverable using the `-h` switch to discover commands, sub-commands, and switches.

```
$ repotools -?

Description:
  A CLI tool with commands to help maintain .NET codebases.

Usage:
  repotools [command] [options]

Options:
  --version       Show version information
  -?, -h, --help  Show help and usage information

Commands:
  nuget  NuGet maintenance commands
```

You can then drill in to reveal sub-commands:

```
$ repotools nuget -?

Description:
  NuGet maintenance commands

Usage:
  repotools nuget [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  upgrade <id> <version>  Upgrade a package dependency, and all transitive dependencies such that no package downgrade warnings occur.
  trim <project>          Removes PackageReference items that are redundant because they are to packages that already appear as transitive dependencies.
```

## Example usage

For example, the following command will upgrade the repo's Directory.Packages.props file to consume a new version of a particular package,
and update all transitive dependencies that also have versions specified in that file, so that you do not have to manually upgrade those versions
to resolve package downgrade errors:

    repotools nuget upgrade StreamJsonRpc 1.2.3
