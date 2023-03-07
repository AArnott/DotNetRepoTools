// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using NuGet.Commands;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// Defines and implements the nuget upgrade command.
/// </summary>
public class UpgradeCommand : MSBuildCommandBase
{
	private const string DirectoryPackagesPropsFileName = "Directory.Packages.props";

	/// <summary>
	/// Initializes a new instance of the <see cref="UpgradeCommand"/> class.
	/// </summary>
	public UpgradeCommand()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="UpgradeCommand"/> class.
	/// </summary>
	/// <inheritdoc cref="CommandBase(InvocationContext)"/>
	public UpgradeCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	/// <summary>
	/// Gets the ID of the package to be upgraded.
	/// </summary>
	required public string PackageId { get; init; }

	/// <summary>
	/// Gets the version to upgrade the package identified by <see cref="PackageId"/> to.
	/// </summary>
	required public string PackageVersion { get; init; }

	/// <summary>
	/// Gets the path to the project file or repo to upgrade.
	/// </summary>
	required public string Path { get; init; }

	/// <summary>
	/// Gets the target framework used to evaluate package dependencies.
	/// </summary>
	required public string TargetFramework { get; init; }

	/// <summary>
	/// Gets a value indicating whether all transitive dependencies will be explicitly added (not just updated as needed if they exist).
	/// </summary>
	public bool Explode { get; init; }

	/// <summary>
	/// Gets the set of version properties to disregard when updating package versions that were previously defined by properties.
	/// </summary>
	public HashSet<string>? DisregardVersionProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<string> packageIdArgument = new Argument<string>("id", "The ID of the root package to be upgraded.");
		Argument<string> packageVersionArgument = new Argument<string>("version", "The version to upgrade to.");
		Option<FileSystemInfo> pathOption = new Option<FileSystemInfo>("--path", "The path to the project or repo to upgrade.") { IsRequired = !File.Exists(DirectoryPackagesPropsFileName) }.ExistingOnly();
		Option<string> frameworkOption = new Option<string>(new[] { "--framework", "-f" }, () => "netstandard2.0", "The target framework used to evaluate package dependencies.");
		Option<bool> explodeOption = new("--explode", "Add PackageVersion items for every transitive dependency, so that they can be added as direct project dependencies as versions are pre-specified.");
		Option<string[]> disregardVersionPropertiesOption = new("--disregard-version-properties", "Specifies one or more MSBuild properties that may be used to define a PackageVersion item's Version attribute that should no longer be referenced. This may be useful when properties have been used for multiple packages and their continued use is problematic because the packages now need their own distinct versions.") { AllowMultipleArgumentsPerToken = true };

		Command command = new("upgrade", "Upgrade a package dependency, and all transitive dependencies such that no package downgrade warnings occur.")
		{
			packageIdArgument,
			packageVersionArgument,
			pathOption,
			frameworkOption,
			explodeOption,
			disregardVersionPropertiesOption,
		};
		command.SetHandler(ctxt => new UpgradeCommand(ctxt)
		{
			PackageId = ctxt.ParseResult.GetValueForArgument(packageIdArgument),
			PackageVersion = ctxt.ParseResult.GetValueForArgument(packageVersionArgument),
			Path = ctxt.ParseResult.GetValueForOption(pathOption)?.FullName ?? Environment.CurrentDirectory,
			TargetFramework = ctxt.ParseResult.GetValueForOption(frameworkOption)!,
			Explode = ctxt.ParseResult.GetValueForOption(explodeOption),
			DisregardVersionProperties = ctxt.ParseResult.GetValueForOption(disregardVersionPropertiesOption)?.ToHashSet(StringComparer.OrdinalIgnoreCase),
		}.ExecuteAndDisposeAsync());

		return command;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteCoreAsync()
	{
		NuGetHelper nuget = new(this.MSBuild, this.Console, this.Path);
		if (!nuget.VerifyCpvmActive())
		{
			this.ExitCode = 1;
			return;
		}

		int versionsUpdated = 1;
		bool topLevelExists = nuget.SetPackageVersion(this.PackageId, this.PackageVersion, addIfMissing: false, disregardVersionProperties: this.DisregardVersionProperties);
		if (!topLevelExists)
		{
			versionsUpdated--;
			this.Console.WriteLine($"No version spec for {this.PackageId} was found. It will not be added, but its dependencies that do have versions specified will be updated where necessary to avoid downgrade warnings as if it were present.");
		}
		else
		{
			nuget.Project.ReevaluateIfNecessary();
		}

		NuGetFramework nugetFramework = NuGetFramework.Parse(this.TargetFramework);
		List<NuGetFramework> targetFrameworks = new() { nugetFramework };
		PackageReference topLevelReference = nuget.CreatePackageReference(this.PackageId, this.PackageVersion, nugetFramework);

		// Visit every transitive dependency and explicitly set it.
		this.CancellationToken.ThrowIfCancellationRequested();

		this.Console.WriteLine("Proactively resolving any introduced package downgrade issues in dependencies.");
		RestoreTargetGraph restoreGraph = await nuget.GetRestoreTargetGraphAsync(new[] { topLevelReference }, targetFrameworks, this.CancellationToken);
		foreach (GraphItem<RemoteResolveResult>? item in restoreGraph.Flattened.Where(i => i.Key.Type == LibraryType.Package))
		{
			if (nuget.SetPackageVersion(item.Key.Name, item.Key.Version.ToFullString(), addIfMissing: this.Explode, allowDowngrade: false, disregardVersionProperties: this.DisregardVersionProperties))
			{
				versionsUpdated++;
			}
		}

		this.Console.WriteLine($"All done. {versionsUpdated} package versions were updated.");
	}
}
