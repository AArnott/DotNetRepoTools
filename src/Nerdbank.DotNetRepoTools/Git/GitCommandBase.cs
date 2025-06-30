// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine.Completions;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Nerdbank.DotNetRepoTools.Git;

internal abstract class GitCommandBase : CommandBase
{
	protected GitCommandBase()
	{
	}

	protected GitCommandBase(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
	}

	protected static IEnumerable<string> GitRefCompletions(CompletionContext context)
	{
		const string LocalBranchPrefix = "refs/heads/";
		const string RemoteBranchPrefix = "refs/remotes/";

		using CancellationTokenSource cts = new(1000);

		foreach (string branch in QueryGitHelper("branch -a --format %(refname)", cts.Token))
		{
			string? simpleName = null;
			if (branch.StartsWith(LocalBranchPrefix))
			{
				simpleName = branch.Substring(LocalBranchPrefix.Length);
			}
			else if (branch.StartsWith(RemoteBranchPrefix))
			{
				simpleName = branch.Substring(RemoteBranchPrefix.Length);
			}

			if (simpleName?.StartsWith(context.WordToComplete, StringComparison.OrdinalIgnoreCase) is true)
			{
				yield return simpleName;
			}
		}
	}

	protected IEnumerable<string> QueryGit(string arguments, CancellationToken cancellationToken)
	{
		if (this.Verbose)
		{
			this.Error.WriteLine($"git {arguments}");
		}

		return QueryGitHelper(arguments, cancellationToken);
	}

	protected async IAsyncEnumerable<string> QueryGitAsync(string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (this.Verbose)
		{
			this.Error.WriteLine($"git {arguments}");
		}

		ProcessStartInfo psi = new("git", arguments)
		{
			RedirectStandardOutput = true,
		};
		using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn git.");
		using (cancellationToken.Register(() => process.Kill()))
		{
			string? line;
			while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
			{
				yield return line;
			}

			await process.WaitForExitAsync(cancellationToken);
		}
	}

	protected async Task<int> ExecGitAsync(string arguments, CancellationToken cancellationToken)
	{
		if (this.Verbose)
		{
			this.Error.WriteLine($"git {arguments}");
		}

		ProcessStartInfo psi = new("git", arguments);
		Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn git.");
		using (cancellationToken.Register(() => process.Kill()))
		{
			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode;
		}
	}

	private static IEnumerable<string> QueryGitHelper(string arguments, CancellationToken cancellationToken)
	{
		ProcessStartInfo psi = new("git", arguments)
		{
			RedirectStandardOutput = true,
		};
		using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn git.");
		using (cancellationToken.Register(() => process.Kill()))
		{
			string? line;
			while ((line = process.StandardOutput.ReadLine()) is not null)
			{
				yield return line;
			}

			process.WaitForExit(500);
			process.Kill();
		}
	}
}
