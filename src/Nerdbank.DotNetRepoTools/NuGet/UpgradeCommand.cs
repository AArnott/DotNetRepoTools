// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Microsoft.Build.Evaluation;
using NuGet.Commands;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// Defines and implements the nuget upgrade command.
/// </summary>
public class UpgradeCommand : CommandBase
{
	private const string DirectoryPackagesPropsFileName = "Directory.Packages.props";

	private readonly MSBuild msbuild = new();

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
	/// Gets the path to the <c>Directory.Packages.props</c> file to be updated.
	/// </summary>
	required public string DirectoryPackagesPropsPath { get; init; }

	/// <summary>
	/// Gets the target framework used to evaluate package dependencies.
	/// </summary>
	required public string TargetFramework { get; init; }

	/// <summary>
	/// Gets a value indicating whether all transitive dependencies will be explicitly added (not just updated as needed if they exist).
	/// </summary>
	public bool Explode { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<string> packageIdArgument = new Argument<string>("id", "The ID of the root package to be upgraded.");
		Argument<string> packageVersionArgument = new Argument<string>("version", "The version to upgrade to.");
		Option<FileInfo> pathOption = new Option<FileInfo>("--path", "The path to the Directory.Packages.props file") { IsRequired = !File.Exists(DirectoryPackagesPropsFileName) }.ExistingOnly();
		Option<string> frameworkOption = new Option<string>("--framework", () => "netstandard2.0", "The target framework used to evaluate package dependencies.");
		Option<bool> explodeOption = new("--explode", "Add PackageVersion items for every transitive dependency, so that they can be added as direct project dependencies as versions are pre-specified.");

		Command command = new("upgrade", "Upgrade a package dependency, and all transitive dependencies such that no package downgrade warnings occur.")
		{
			packageIdArgument,
			packageVersionArgument,
			pathOption,
			frameworkOption,
			explodeOption,
		};
		command.SetHandler(ctxt => new UpgradeCommand(ctxt)
		{
			PackageId = ctxt.ParseResult.GetValueForArgument(packageIdArgument),
			PackageVersion = ctxt.ParseResult.GetValueForArgument(packageVersionArgument),
			DirectoryPackagesPropsPath = ctxt.ParseResult.GetValueForOption(pathOption)?.FullName ?? Path.GetFullPath(DirectoryPackagesPropsFileName),
			TargetFramework = ctxt.ParseResult.GetValueForOption(frameworkOption)!,
			Explode = ctxt.ParseResult.GetValueForOption(explodeOption),
		}.ExecuteAsync());

		return command;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteCoreAsync()
	{
		Project packagesProps = this.msbuild.EvaluateProjectFile(this.DirectoryPackagesPropsPath);
		NuGetHelper nuget = new(this.Console, packagesProps);
		int versionsUpdated = 1;

		bool topLevelExists = nuget.SetPackageVersion(this.PackageId, this.PackageVersion, addIfMissing: false);
		if (!topLevelExists)
		{
			versionsUpdated--;
			this.Console.WriteLine($"No version spec for {this.PackageId} was found. It will not be added, but its dependencies that do have versions specified will be updated where necessary to avoid downgrade warnings as if it were present.");
		}

		NuGetFramework nugetFramework = NuGetFramework.Parse(this.TargetFramework);
		List<NuGetFramework> targetFrameworks = new() { nugetFramework };
		PackageReference topLevelReference = nuget.CreatePackageReference(this.PackageId, this.PackageVersion, nugetFramework);

		// Visit every transitive dependency and explicitly set it.
		this.CancellationToken.ThrowIfCancellationRequested();

		this.Console.WriteLine("Proactively resolving any introduced package downgrade issues in dependencies.");
		RestoreTargetGraph restoreGraph = await nuget.GetRestoreTargetGraphAsync(new[] { topLevelReference }, targetFrameworks, this.CancellationToken);
		foreach (GraphItem<RemoteResolveResult>? item in restoreGraph.Flattened)
		{
			if (nuget.SetPackageVersion(item.Key.Name, item.Key.Version.ToFullString(), addIfMissing: this.Explode, allowDowngrade: false))
			{
				versionsUpdated++;
			}
		}

		this.Console.WriteLine($"All done. {versionsUpdated} package versions were updated.");
		packagesProps.Save();
	}
}
