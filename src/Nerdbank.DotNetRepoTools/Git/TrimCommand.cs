// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Nerdbank.DotNetRepoTools.Git;

internal class TrimCommand : GitCommandBase
{
	public TrimCommand()
	{
	}

	public TrimCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
	}

	/// <summary>
	/// Gets the ref of the object that is the ultimate object of any branch.
	/// Once a branch has merged into this ref, it can be deleted.
	/// </summary>
	required public string MergedInto { get; init; }

	/// <summary>
	/// Gets a value indicating whether to delete local branches that have been merged into <see cref="MergedInto"/>.
	/// </summary>
	public bool TrimLocalBranches { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<string> mergedIntoArg = new("mergeTarget", "Branches become trimmable after they have been merged into this ref. Typically origin/main or similar.");
		mergedIntoArg.AddCompletions(GitRefCompletions);
		Command command = new("trim", "Removes local branches that have already been merged into some target ref.")
		{
			mergedIntoArg,
		};
		command.SetHandler(ctxt => new TrimCommand(ctxt)
		{
			MergedInto = ctxt.ParseResult.GetValueForArgument(mergedIntoArg),
		}.ExecuteAndDisposeAsync());

		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		const string LocalBranchPrefix = "refs/heads/";
		await foreach (string branch in QueryGitAsync($"git branch --merged {this.MergedInto} --format %(refname)", this.CancellationToken))
		{
			if (branch.StartsWith(LocalBranchPrefix))
			{
				string branchName = branch.Substring(LocalBranchPrefix.Length);
				await ExecGitAsync($"git branch -d {branchName}", this.CancellationToken);
			}
		}
	}
}
