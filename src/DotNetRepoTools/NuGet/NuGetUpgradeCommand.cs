// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace DotNetRepoTools.NuGet;

/// <summary>
/// Defines and implements the nuget upgrade command.
/// </summary>
internal class NuGetUpgradeCommand
{
	private const string DirectoryPackagesPropsFileName = "Directory.Packages.props";
	private static readonly Argument<string> PackageIdArgument = new("id", "The ID of the root package to be upgraded.");
	private static readonly Argument<string> PackageVersionArgument = new("version", "The version to upgrade to.");
	private static readonly Option<FileInfo> PathOption = new Option<FileInfo>("--path", "The path to the Directory.Packages.props file to update.") { IsRequired = !File.Exists(DirectoryPackagesPropsFileName) }.ExistingOnly();

	private NuGetUpgradeCommand(InvocationContext invocationContext)
	{
		this.InvocationContext = invocationContext;
		this.PackageId = invocationContext.ParseResult.GetValueForArgument(PackageIdArgument);
		this.PackageVersion = invocationContext.ParseResult.GetValueForArgument(PackageVersionArgument);
		this.DirectoryPackagesPropsPath = invocationContext.ParseResult.GetValueForOption(PathOption)?.FullName ?? throw new Exception("Missing option");
		this.CancellationToken = invocationContext.GetCancellationToken();
	}

	internal InvocationContext InvocationContext { get; }

	internal string PackageId { get; }

	internal string PackageVersion { get; }

	internal string DirectoryPackagesPropsPath { get; }

	internal CancellationToken CancellationToken { get; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Command nugetUpgrade = new("upgrade", "Upgrade a package dependency, and all transitive dependencies such that no package downgrade warnings occur.")
		{
			PackageIdArgument,
			PackageVersionArgument,
			PathOption,
		};
		nugetUpgrade.SetHandler(ctxt => new NuGetUpgradeCommand(ctxt).Execute());

		return nugetUpgrade;
	}

	internal void Execute()
	{
		Console.Error.WriteLine("This command has not yet been implemented.");
		this.InvocationContext.ExitCode = 1;
	}
}
