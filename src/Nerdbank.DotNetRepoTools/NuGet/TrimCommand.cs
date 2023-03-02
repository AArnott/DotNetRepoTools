// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class TrimCommand : CommandBase
{
	public TrimCommand()
	{
	}

	public TrimCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	required public string Project { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<FileInfo> projectArgument = new Argument<FileInfo>("project", "The path to the project file with the PackageReference items to trim.").ExistingOnly();
		Command command = new("trim", "Removes PackageReference items that are redundant because they are to packages that already appear as transitive dependencies.")
		{
			projectArgument,
		};
		command.SetHandler(ctxt => new TrimCommand(ctxt)
		{
			Project = ctxt.ParseResult.GetValueForArgument(projectArgument).FullName,
		}.ExecuteAsync());

		return command;
	}

	protected override Task ExecuteCoreAsync()
	{
		throw new NotImplementedException();
	}
}
