// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class TrimCommand : MSBuildCommandBase
{
	public TrimCommand()
	{
	}

	public TrimCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	public required string Project { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Option<FileInfo> pathOption = new Option<FileInfo>("--path", "The path to the project file with the PackageReference items to trim.").ExistingOnly();
		Command command = new("trim", "Removes PackageReference items that are redundant because they are to packages that already appear as transitive dependencies.")
		{
			pathOption,
		};
		command.SetHandler(ctxt => new TrimCommand(ctxt)
		{
			Project = ctxt.ParseResult.GetValueForOption(pathOption)?.FullName ?? Environment.CurrentDirectory,
		}.ExecuteAndDisposeAsync());

		return command;
	}

	protected override Task ExecuteCoreAsync()
	{
		throw new NotImplementedException();
	}
}
