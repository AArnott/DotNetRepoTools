// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestReviewerVoteCommand : PullRequestReviewerCommandBase
{
	private static readonly Argument<Vote> VoteArgument = new("vote") { Description = "The vote to cast on the specified pull request.", Arity = ArgumentArity.ExactlyOne };

	private static readonly Option<string> IdOption = new("--id") { Description = "The ID of the account to cast a vote on behalf of. May be the account ID (guid) or user@domain.com." };

	public PullRequestReviewerVoteCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestReviewerVoteCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.VoteToCast = parseResult.GetValue(VoteArgument);
		this.Id = parseResult.GetValue(IdOption);
	}

	public enum Vote
	{
#pragma warning disable SA1602 // Enumeration items should be documented
		Approved = 10,
		ApprovedWithSuggestions = 5,
		NoVote = 0,
		WaitingForAuthor = -5,
		Rejected = -10,
#pragma warning restore SA1602 // Enumeration items should be documented
	}

	public required Vote VoteToCast { get; init; }

	public string? Id { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("vote", "Vote (approve or reject) a pull request.")
		{
			VoteArgument,
			IdOption,
		};
		AddCommonOptions(command);

		command.SetAction((parseResult, cancellationToken) => new PullRequestReviewerVoteCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		string? id = this.Id;
		if (!string.IsNullOrEmpty(id) && !Guid.TryParse(id, out _))
		{
			id = await this.LookupIdentityAsync(id);
			if (id is null)
			{
				this.Error.WriteLine($"Unable to translate {this.Id} into an identity.");
				this.ExitCode = 2;
				return;
			}
		}

		if (id is null)
		{
			id = await this.WhoAmIAsync();
		}

		if (id is null)
		{
			this.Error.WriteLine("Unable to find your identity to cast a vote.");
			this.ExitCode = 1;
			return;
		}

		HttpRequestMessage request = new(HttpMethod.Put, $"{id}?api-version=7.1")
		{
			Content = JsonContent.Create(
				new
				{
					vote = (int)this.VoteToCast,
				},
				mediaType: new("application/json")),
		};

		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: true);
		if (this.IsSuccessResponse(response))
		{
			this.Out.WriteLine("OK");
		}
	}
}
