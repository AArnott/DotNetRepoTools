// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Creates a new comment thread on a pull request.
/// </summary>
/// <remarks>
/// See <see href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-threads/create?view=azure-devops-rest-7.1">REST API documentation</see>.
/// </remarks>
internal class PullRequestCommentCommand : PullRequestCommandBase
{
	protected static readonly Argument<string> CommentArgument = new("comment", "The comment to post. Markdown format. If not specified, STDIN will be used.") { Arity = ArgumentArity.ZeroOrOne };

	protected static readonly Option<PullRequestCommentState> StateOption = new("--state", "The state to set the new comment.");

	public PullRequestCommentCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestCommentCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.Comment = invocationContext.ParseResult.GetValueForArgument(CommentArgument);
		this.State = invocationContext.ParseResult.GetValueForOption(StateOption);
	}

	public required string? Comment { get; init; }

	public PullRequestCommentState State { get; init; } = PullRequestCommentState.Unknown;

	internal static new Command CreateCommand()
	{
		Command command = new("comment", "Post a comment on a pull request.");
		command.AddArgument(CommentArgument);
		command.AddOption(StateOption);
		AddCommonOptions(command);

		command.SetHandler(ctxt => new PullRequestCommentCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		string? comment = this.Comment;
		if (comment is null)
		{
			comment = string.Empty;
			string? line;
			while ((line = System.Console.ReadLine()) is not null)
			{
				comment += line + Environment.NewLine;
			}
		}

		HttpContent content = JsonContent.Create(new
		{
			comments = new[]
			{
				new
				{
					parentCommentId = 0,
					commentType = 1,
					content = comment,
				},
			},
			status = (int)this.State,
		});

		HttpRequestMessage requestMessage = new(HttpMethod.Post, "threads?api-version=7.1")
		{
			Content = content,
		};
		if (this.WhatIf)
		{
			await this.WriteWhatIfAsync(requestMessage);
			return;
		}

		HttpResponseMessage response = await this.HttpClient.SendAsync(requestMessage);
		if (response.IsSuccessStatusCode)
		{
			this.Console.WriteLine("Comment posted.");
		}
		else
		{
			this.Console.Error.WriteLine($"Failed to post comment ({(int)response.StatusCode} {response.StatusCode}):");
			this.Console.Error.WriteLine(await response.Content.ReadAsStringAsync(this.CancellationToken));

			if (this.InvocationContext is not null)
			{
				this.InvocationContext.ExitCode = (int)response.StatusCode;
			}
		}
	}
}
