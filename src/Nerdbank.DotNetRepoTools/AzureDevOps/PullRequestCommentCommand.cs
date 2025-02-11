// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Creates a new comment thread on a pull request.
/// </summary>
/// <remarks>
/// See <see href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-threads/create?view=azure-devops-rest-7.1">REST API documentation</see>.
/// </remarks>
internal class PullRequestCommentCommand : PullRequestModifyingCommandBase
{
	protected static readonly Argument<string> CommentArgument = new("comment", "The comment to post. Markdown format. If not specified, STDIN will be used.") { Arity = ArgumentArity.ZeroOrOne };

	protected static readonly Option<CommentType> TypeOption = new("--type", () => CommentType.Text, "The type of comment to post.");

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
		this.Type = invocationContext.ParseResult.GetValueForOption(TypeOption);
	}

	public enum CommentType
	{
		/// <summary>
		/// The comment comes as a result of a code change.
		/// </summary>
		CodeChange,

		/// <summary>
		/// The comment represents a system message.
		/// </summary>
		System,

		/// <summary>
		/// This is a regular user comment.
		/// </summary>
		Text,

		/// <summary>
		/// The comment type is not known.
		/// </summary>
		Unknown,
	}

	public required string? Comment { get; init; }

	public PullRequestCommentState State { get; init; } = PullRequestCommentState.Unknown;

	public CommentType Type { get; init; } = CommentType.Text;

	internal static new Command CreateCommand()
	{
		Command command = new("comment", "Post a comment on a pull request.");
		command.AddArgument(CommentArgument);
		command.AddOption(StateOption);
		command.AddOption(TypeOption);
		AddCommonOptions(command);

		command.SetHandler(ctxt => new PullRequestCommentCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		HttpRequestMessage requestMessage = new(HttpMethod.Post, "threads?api-version=7.1")
		{
			Content = JsonContent.Create(new
			{
				comments = new[]
				{
					new
					{
						parentCommentId = 0,
						commentType = CamelCase(this.Type.ToString()),
						content = this.Comment ?? this.ReadFromStandardIn("Enter comment for pull request."),
					},
				},
				status = (int)this.State,
			}),
		};
		HttpResponseMessage? response = await this.SendAsync(requestMessage, canReadContent: true);
		if (response is { IsSuccessStatusCode: true })
		{
			this.Console.WriteLine("OK");
		}
	}
}
