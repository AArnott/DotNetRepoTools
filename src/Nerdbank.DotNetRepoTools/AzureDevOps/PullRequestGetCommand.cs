// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal class PullRequestGetCommand : PullRequestCommandBase
{
	protected static readonly Argument<int> PullRequestIdArgument = new("id") { Description = "The ID of the pull request." };

	public PullRequestGetCommand()
	{
	}

	[SetsRequiredMembers]
	public PullRequestGetCommand(ParseResult parseResult, CancellationToken cancellationToken = default)
		: base(parseResult, cancellationToken)
	{
		this.PullRequestId = parseResult.GetValue(PullRequestIdArgument);
	}

	public int PullRequestId { get; init; }

	internal static new Command CreateCommand()
	{
		Command command = new("get", "Get details about a pull request.")
		{
			PullRequestIdArgument,
		};
		AddCommonOptions(command);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			using var cmd = new PullRequestGetCommand(parseResult, cancellationToken);
			await cmd.ExecuteAsync();
			return cmd.ExitCode;
		});
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
			this.Out.WriteLine(json);
		}
		else
		{
			await this.PrintErrorMessageAsync(response);
		}
	}
}
