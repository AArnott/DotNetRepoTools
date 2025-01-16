# Getting Started

## Acquisition

Consume this library via its NuGet Package.
Click on the badge to find its latest version and the instructions for consuming it that best apply to your project.

[![NuGet package](https://img.shields.io/nuget/v/Nerdbank.DotNetRepoTools.svg)](https://www.nuget.org/packages/Nerdbank.DotNetRepoTools)

Install or upgrade this tool with the following command:

    dotnet tool update -g Nerdbank.DotNetRepoTools

Install or upgrade to the latest CI build with the following command:

    dotnet tool update -g Nerdbank.DotNetRepoTools --prerelease --add-source https://pkgs.dev.azure.com/andrewarnott/OSS/_packaging/PublicCI/nuget/v3/index.json

### Consuming CI builds

You can acquire CI build packages (with no assurance of quality) to get early access to the latest changes without waiting for the next release to nuget.org.

There are two feeds you can use to acquire these packages:

- [GitHub Packages](https://github.com/AArnott?tab=packages&repo_name=DotNetRepoTools) (requires GitHub authentication)
- [Azure Artifacts](https://dev.azure.com/andrewarnott/OSS/_artifacts/feed/PublicCI) (no authentication required)

## Usage

### Commands

After install, use the tool name `repo` to run commands.

This CLI tool has (or will have) a variety of commands and sub-commands, discoverable using the `-h` switch to discover commands, sub-commands, and switches.

```text
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
  azdo   Azure DevOps operations
```

You can then drill in to reveal sub-commands:

```text
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

```text
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

### Example usage

For example, the following command will upgrade the repo's Directory.Packages.props file to consume a new version of a particular package,
and update all transitive dependencies that also have versions specified in that file, so that you do not have to manually upgrade those versions
to resolve package downgrade errors:

```text
repo nuget upgrade StreamJsonRpc 1.2.3
```

Or the command I use most frequently, which cleans up all your stale local branches:

```text
repo git trim origin/main
```
