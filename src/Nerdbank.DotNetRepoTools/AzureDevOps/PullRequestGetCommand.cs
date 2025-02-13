// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestGetCommand : PullRequestCommandBase
{
	protected static readonly Argument<int> PullRequestIdArgument = new("id", "The ID of the pull request.") { Arity = ArgumentArity.ExactlyOne };

	public PullRequestGetCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestGetCommand(InvocationContext invocationContext)
		: base(invocationContext)
	{
		this.PullRequestId = invocationContext.ParseResult.GetValueForArgument(PullRequestIdArgument);
	}

	public int PullRequestId { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("get", "Get details about a pull request.")
		{
			PullRequestIdArgument,
		};
		AddCommonOptions(command);

		command.SetHandler(ctxt => new PullRequestGetCommand(ctxt).ExecuteAndDisposeAsync());
		return command;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = base.CreateHttpClient();
		client.BaseAddress = new Uri($"{client.BaseAddress!.AbsoluteUri}/");
		return client;
	}

	protected override async Task ExecuteCoreAsync()
	{
		HttpRequestMessage request = new(HttpMethod.Get, $"{this.PullRequestId}?api-version=7.1");
		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: false);
		if (this.IsSuccessResponse(response))
		{
			string json = await response.Content.ReadAsStringAsync(this.CancellationToken);
			this.Console.WriteLine(json);
		}
		else
		{
			await this.PrintErrorMessageAsync(response);
		}
	}
}
