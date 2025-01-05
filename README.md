# Nerdbank.DotNetRepoTools

***A CLI toolbox for repo maintenance***

[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.DotNetRepoTools.svg)](https://www.nuget.org/packages/Nerdbank.DotNetRepoTools)

[![🏭 Build](https://github.com/AArnott/DotNetRepoTools/actions/workflows/build.yml/badge.svg)](https://github.com/AArnott/DotNetRepoTools/actions/workflows/build.yml)

## Documentation

Check out our [full documentation](https://aarnott.github.io/DotNetRepoTools/docs/getting-started.html).

## Usage

### Acquisition

Install or upgrade this tool with the following command:

    dotnet tool update -g Nerdbank.DotNetRepoTools

Install or upgrade to the latest CI build with the following command:

    dotnet tool update -g Nerdbank.DotNetRepoTools --prerelease --add-source https://pkgs.dev.azure.com/andrewarnott/OSS/_packaging/PublicCI/nuget/v3/index.json

## Commands

After install, use the tool name `repo` to run commands.

This CLI tool has (or will have) a variety of commands and sub-commands, discoverable using the `-h` switch to discover commands, sub-commands, and switches.

```
$ repo -?

Description:
  A CLI tool with commands to help maintain .NET codebases.

Usage:
  repotools [command] [options]

Options:
  --version       Show version information
  -?, -h, --help  Show help and usage information

Commands:
  nuget  NuGet maintenance commands
  git    Git repo maintenance workflows
```

You can then drill in to reveal sub-commands:

```
$ repo nuget -?

Description:
  NuGet maintenance commands

Usage:
  repotools nuget [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  reconcile-versions              Resolves all package downgrade warnings.
  upgrade <id> <version>          Upgrade a package dependency, and all transitive dependencies such that no package downgrade warnings occur.
  trim                            Removes PackageReference items that are redundant because they are to packages that already appear as transitive
                                  dependencies.
  ManagePackageVersionsCentrally  Migrates a repo to use centralized package versions.
```

and

```
$ repo git -?

Description:
  Git repo maintenance workflows

Usage:
  repotools git [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  trim <mergeTarget>  Removes local branches that have already been merged into some target ref.
```

## Example usage

For example, the following command will upgrade the repo's Directory.Packages.props file to consume a new version of a particular package,
and update all transitive dependencies that also have versions specified in that file, so that you do not have to manually upgrade those versions
to resolve package downgrade errors:

    repo nuget upgrade StreamJsonRpc 1.2.3

Or the command I use most frequently, which cleans up all your stale local branches:

    repo git trim origin/main
