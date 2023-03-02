// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Build.Evaluation;

namespace DotNetRepoTools.NuGet;

/// <summary>
/// Defines and implements the nuget upgrade command.
/// </summary>
public class UpgradeCommand : CommandBase
{
	private const string DirectoryPackagesPropsFileName = "Directory.Packages.props";
	private const string PackageVersionItemType = "PackageVersion";

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
	/// <param name="invocationContext">A command line invocation from which to initialize.</param>
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
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<string> packageIdArgument = new Argument<string>("id", "The ID of the root package to be upgraded.");
		Argument<string> packageVersionArgument = new Argument<string>("version", "The version to upgrade to.");
		Option<FileInfo> pathOption = new Option<FileInfo>("--path", "The path to the Directory.Packages.props file to update.") { IsRequired = !File.Exists(DirectoryPackagesPropsFileName) }.ExistingOnly();

		Command command = new("upgrade", "Upgrade a package dependency, and all transitive dependencies such that no package downgrade warnings occur.")
		{
			packageIdArgument,
			packageVersionArgument,
			pathOption,
		};
		command.SetHandler(ctxt => new UpgradeCommand(ctxt)
		{
			PackageId = ctxt.ParseResult.GetValueForArgument(packageIdArgument),
			PackageVersion = ctxt.ParseResult.GetValueForArgument(packageVersionArgument),
			DirectoryPackagesPropsPath = ctxt.ParseResult.GetValueForOption(pathOption)?.FullName ?? throw new Exception("Missing option"),
		}.ExecuteAsync());

		return command;
	}

	/// <inheritdoc/>
	protected override Task ExecuteCoreAsync()
	{
		Project packagesProps = this.msbuild.EvaluateProjectFile(this.DirectoryPackagesPropsPath);

		ICollection<ProjectItem> packageVersionItems = packagesProps.GetItems(PackageVersionItemType);
		ProjectItem? packageVersionTargetItem = packageVersionItems.FirstOrDefault(i => string.Equals(i.EvaluatedInclude, this.PackageId, StringComparison.OrdinalIgnoreCase));
		if (packageVersionTargetItem is not null)
		{
			packageVersionTargetItem.SetMetadataValue("Version", this.PackageVersion);
		}

		packagesProps.Save();
		return Task.CompletedTask;
	}
}
