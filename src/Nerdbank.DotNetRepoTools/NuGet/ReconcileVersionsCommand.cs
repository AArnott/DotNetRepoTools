// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using NuGet.Frameworks;

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// Resolves package downgrade warnings for a project.
/// </summary>
public class ReconcileVersionsCommand : MSBuildCommandBase
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ReconcileVersionsCommand"/> class.
	/// </summary>
	public ReconcileVersionsCommand()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ReconcileVersionsCommand"/> class.
	/// </summary>
	/// <inheritdoc cref="CommandBase(ParseResult, CancellationToken)"/>
	public ReconcileVersionsCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
	}

	/// <summary>
	/// Gets the path to the project file to reconcile versions for.
	/// </summary>
	public required string ProjectPath { get; init; }

	/// <summary>
	/// Gets the target framework used to evaluate package dependencies.
	/// </summary>
	public required string TargetFramework { get; init; }

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
		Option<FileSystemInfo> pathOption = new Option<FileSystemInfo>("--path") { Description = "The path to the project or repo to resolve version issues with." }.AcceptExistingOnly();
		Option<string> frameworkOption = new Option<string>("--framework", "-f") { DefaultValueFactory = _ => "netstandard2.0", Description = "The target framework used to evaluate package dependencies." };
		Option<string[]> disregardVersionPropertiesOption = new("--disregard-version-properties") { Description = "Specifies one or more MSBuild properties that may be used to define a PackageVersion item's Version attribute that should no longer be referenced. This may be useful when properties have been used for multiple packages and their continued use is problematic because the packages now need their own distinct versions.", AllowMultipleArgumentsPerToken = true };

		Command command = new("reconcile-versions", "Resolves all package downgrade warnings.")
		{
			pathOption,
			frameworkOption,
			disregardVersionPropertiesOption,
		};
		command.SetAction((parseResult, cancellationToken) => new ReconcileVersionsCommand(parseResult, cancellationToken)
		{
			ProjectPath = parseResult.GetValue(pathOption)?.FullName ?? Environment.CurrentDirectory,
			TargetFramework = parseResult.GetValue(frameworkOption)!,
			DisregardVersionProperties = parseResult.GetValue(disregardVersionPropertiesOption)?.ToHashSet(StringComparer.OrdinalIgnoreCase),
		}.ExecuteAndDisposeAsync());

		return command;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteCoreAsync()
	{
		NuGetHelper nuget = new(this.MSBuild, this.ProjectPath) { Out = this.Out, Error = this.Error };

		NuGetFramework nugetFramework = NuGetFramework.Parse(this.TargetFramework);
		int versionsUpdated = await nuget.CorrectDowngradeIssuesAsync(nugetFramework, null, this.DisregardVersionProperties, this.CancellationToken);
		this.Out.WriteLine($"All done. {versionsUpdated} package versions were updated.");
	}
}
