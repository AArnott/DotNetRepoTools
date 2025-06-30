// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace Nerdbank.DotNetRepoTools.Git;

internal class TrimCommand : GitCommandBase
{
	public TrimCommand()
	{
	}

	public TrimCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
	}

	/// <summary>
	/// Gets the ref of the object that is the ultimate object of any branch.
	/// Once a branch has merged into this ref, it can be deleted.
	/// </summary>
	public required string MergedInto { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<string> mergedIntoArg = new("mergeTarget")
		{
			Description = "Branches become trimmable after they have been merged into this ref. Typically origin/main or similar.",
			HelpName = "mergeTarget",
		};
		mergedIntoArg.CompletionSources.Add(GitRefCompletions);
		Command command = new("trim", "Removes local branches that have already been merged into some target ref. Squashed branches can sometimes also be detected.")
		{
			mergedIntoArg,
		};
		AddCommonOptions(command);
		command.SetAction((parseResult, cancellationToken) => new TrimCommand(parseResult, cancellationToken)
		{
			MergedInto = parseResult.GetValue(mergedIntoArg)!,
		}.ExecuteAndDisposeAsync());

		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		const string LocalBranchPrefix = "refs/heads/";

		List<string> branchesToDelete = [];
		await foreach (string branch in this.QueryGitAsync($"branch --merged {this.MergedInto} --format %(refname)", this.CancellationToken))
		{
			// Skip output such as "(HEAD detached at origin/main)"
			if (branch.StartsWith(LocalBranchPrefix))
			{
				branchesToDelete.Add(branch.Substring(LocalBranchPrefix.Length));
			}
		}

		await foreach (string branch in this.QueryGitAsync($"branch --no-merged {this.MergedInto} --format %(refname)", this.CancellationToken))
		{
			// Skip output such as "(HEAD detached at origin/main)"
			if (branch.StartsWith(LocalBranchPrefix))
			{
				bool novelCommitsFound = false;
				await foreach (string line in this.QueryGitAsync($"cherry {this.MergedInto} {branch}", this.CancellationToken))
				{
					if (line.StartsWith("+"))
					{
						novelCommitsFound = true;
						break;
					}
				}

				if (!novelCommitsFound)
				{
					branchesToDelete.Add(branch.Substring(LocalBranchPrefix.Length));
				}
			}
		}

		branchesToDelete.Sort(StringComparer.OrdinalIgnoreCase);
		if (this.WhatIf)
		{
			this.Out.WriteLine("The following branches are trimmable:");
			foreach (string branch in branchesToDelete)
			{
				this.Out.WriteLine($"  {branch}");
			}
		}
		else
		{
			StringBuilder branchListAsString = new();
			const string DeleteBranchCommandPrefix = "branch -D ";
			while (branchesToDelete.Count > 0)
			{
				// Greedily delete many branches at once provided the command line doesn't get so long that it tends to fail.
				branchListAsString.Clear();
				while (branchesToDelete.Count > 0 && DeleteBranchCommandPrefix.Length + branchListAsString.Length + 1 + branchesToDelete[0].Length < 8192)
				{
					branchListAsString.Append(' ');
					branchListAsString.Append(branchesToDelete[0]);
					branchesToDelete.RemoveAt(0);
				}

				await this.ExecGitAsync($"{DeleteBranchCommandPrefix}{branchListAsString}", this.CancellationToken);
			}
		}
	}
}
