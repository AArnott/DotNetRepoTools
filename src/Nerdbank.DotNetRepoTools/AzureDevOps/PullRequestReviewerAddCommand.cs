// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestReviewerAddCommand : PullRequestReviewerCommandBase
{
	protected static readonly Argument<string[]> OptionalReviewersArgument = new("optional-reviewers", "The users to add as reviewers to the pull request. These should take the form of user@domain.com.") { Arity = ArgumentArity.ZeroOrMore };

	protected static readonly Option<string[]> RequiredReviewersOption = new("--required", "The users to add as required reviewers to the pull request. These should take the form of user@domain.com. All users listed after this switch will be required.") { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = true };

	public PullRequestReviewerAddCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestReviewerAddCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.OptionalReviewers = invocationContext.ParseResult.GetValueForArgument(OptionalReviewersArgument)!;
		this.RequiredReviewers = invocationContext.ParseResult.GetValueForOption(RequiredReviewersOption)!;
	}

	public required string[] OptionalReviewers { get; init; }

	public required string[] RequiredReviewers { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("add", "Adds reviewers to a pull request.");
		command.AddArgument(OptionalReviewersArgument);
		command.AddOption(RequiredReviewersOption);
		AddCommonOptions(command);

		command.SetHandler(ctxt => new PullRequestReviewerAddCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override async Task ExecuteCoreAsync()
	{
		if (this.RequiredReviewers.Length == 0 && this.OptionalReviewers.Length == 0)
		{
			this.Console.Error.WriteLine("No reviewers to add.");
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
			this.Console.Write($"Adding {reviewer} as a{optReq} reviewer...");

			string? id = await this.LookupIdentityAsync(reviewer);
			if (id is null)
			{
				this.Console.WriteLine("NOT FOUND.");
				return;
			}

			HttpRequestMessage request = new(HttpMethod.Put, $"{id}?api-version=7.1")
			{
				Content = JsonContent.Create(
					new
					{
						isRequired = required,
					},
					mediaType: new("application/json")),
			};

			HttpResponseMessage? response = await this.SendAsync(request, canReadContent: true);
			if (this.IsSuccessResponse(response))
			{
				this.Console.WriteLine("OK");
			}
		}
	}
}
