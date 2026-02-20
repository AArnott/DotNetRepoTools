// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestReviewerAddCommand : PullRequestReviewerCommandBase
{
	protected static readonly Argument<string[]> OptionalReviewersArgument = new("optional-reviewers") { Description = "The users to add as reviewers to the pull request. These should take the form of user@domain.com.", Arity = ArgumentArity.ZeroOrMore };

	protected static readonly Option<string[]> RequiredReviewersOption = new("--required") { Description = "The users to add as required reviewers to the pull request. These should take the form of user@domain.com. All users listed after this switch will be required.", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = true };

	public PullRequestReviewerAddCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestReviewerAddCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.OptionalReviewers = parseResult.GetValue(OptionalReviewersArgument)!;
		this.RequiredReviewers = parseResult.GetValue(RequiredReviewersOption)!;
	}

	public required string[] OptionalReviewers { get; init; }

	public required string[] RequiredReviewers { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("add", "Adds reviewers to a pull request.");
		command.Arguments.Add(OptionalReviewersArgument);
		command.Options.Add(RequiredReviewersOption);
		AddCommonOptions(command);

		command.SetAction((parseResult, cancellationToken) => new PullRequestReviewerAddCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		if (this.RequiredReviewers.Length == 0 && this.OptionalReviewers.Length == 0)
		{
			this.Error.WriteLine("No reviewers to add.");
		}

		foreach (string required in this.RequiredReviewers)
		{
			await AddReviewerAsync(required, required: true);
		}

		foreach (string optional in this.OptionalReviewers)
		{
			await AddReviewerAsync(optional, required: false);
		}

		async Task AddReviewerAsync(string reviewer, bool required)
		{
			string optReq = required ? " required" : "n optional";
			this.Out.Write($"Adding {reviewer} as a{optReq} reviewer...");

			string? id = await this.LookupIdentityAsync(reviewer);
			if (id is null)
			{
				this.Out.WriteLine("NOT FOUND.");
				return;
			}

			HttpRequestMessage request = new(HttpMethod.Put, $"{id}?api-version=7.1")
			{
				Content = JsonContent.Create(
					new JsonObject { ["isRequired"] = required },
					SourceGenerationContext.Default.JsonNode,
					mediaType: new("application/json")),
			};

			HttpResponseMessage? response = await this.SendAsync(request, canReadContent: true);
			if (this.IsSuccessResponse(response))
			{
				this.Out.WriteLine("OK");
			}
		}
	}
}
