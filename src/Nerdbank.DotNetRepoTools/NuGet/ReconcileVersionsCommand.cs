// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
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
	/// <inheritdoc cref="CommandBase(InvocationContext)"/>
	public ReconcileVersionsCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	/// <summary>
	/// Gets the path to the project file to reconcile versions for.
	/// </summary>
	required public string ProjectPath { get; init; }

	/// <summary>
	/// Gets the target framework used to evaluate package dependencies.
	/// </summary>
	required public string TargetFramework { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<FileInfo> projectArgument = new Argument<FileInfo>("project", "The path to the project or repo to resolve version issues with.").ExistingOnly();
		Option<string> frameworkOption = new Option<string>("--framework", () => "netstandard2.0", "The target framework used to evaluate package dependencies.");

		Command command = new("reconcile-versions", "Resolves all package downgrade warnings.")
		{
			projectArgument,
			frameworkOption,
		};
		command.SetHandler(ctxt => new ReconcileVersionsCommand(ctxt)
		{
			ProjectPath = ctxt.ParseResult.GetValueForArgument(projectArgument).FullName,
			TargetFramework = ctxt.ParseResult.GetValueForOption(frameworkOption)!,
		}.ExecuteAndDisposeAsync());

		return command;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteCoreAsync()
	{
		NuGetHelper nuget = new(this.MSBuild, this.Console, this.ProjectPath);

		NuGetFramework nugetFramework = NuGetFramework.Parse(this.TargetFramework);
		int versionsUpdated = await nuget.CorrectDowngradeIssuesAsync(nugetFramework, null, this.CancellationToken);
		this.Console.WriteLine($"All done. {versionsUpdated} package versions were updated.");
	}
}
