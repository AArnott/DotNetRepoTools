// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class TrimCommand : MSBuildCommandBase
{
	public TrimCommand()
	{
	}

	public TrimCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
	}

	public required string Project { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Option<FileInfo> pathOption = new Option<FileInfo>("--path") { Description = "The path to the project file with the PackageReference items to trim." }.AcceptExistingOnly();
		Command command = new("trim")
		{
			Description = "Removes PackageReference items that are redundant because they are to packages that already appear as transitive dependencies.",
			Options =
			{
				pathOption,
			},
		};
		command.SetAction((parseResult, cancellationToken) => new TrimCommand(parseResult, cancellationToken)
		{
			Project = parseResult.GetValue(pathOption)?.FullName ?? Environment.CurrentDirectory,
		}.ExecuteAndDisposeAsync());

		return command;
	}

	protected override Task ExecuteCoreAsync()
	{
		throw new NotImplementedException();
	}
}
