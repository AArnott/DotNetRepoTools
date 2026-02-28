// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// The output format for the <see cref="BranchPushesCommand"/>.
/// </summary>
internal enum OutputFormat
{
	/// <summary>Human-readable text output (default).</summary>
	Text,

	/// <summary>JSON output as returned by the Azure DevOps REST API.</summary>
	Json,
}

/// <summary>
/// Lists recent pushes to a branch.
/// </summary>
internal class BranchPushesCommand : BranchCommandBase
{
	/// <summary>The default (and maximum) number of results to request from AzDO list endpoints.</summary>
	private const int DefaultTop = 100;

	private static readonly Argument<string> BranchNameArgument = new("branch-name")
	{
		Description = "The name of the branch to list pushes for.",
		Arity = ArgumentArity.ExactlyOne,
	};

	private static readonly Option<int?> TopOption = new("--top")
	{
		Description = $"The maximum number of pushes to return. Defaults to {DefaultTop}.",
	};

	private static readonly Option<DateTimeOffset?> BeforeOption = new("--before")
	{
		Description = "Return only pushes that occurred before this timestamp (e.g. 2024-01-15T12:00:00Z).",
	};

	private static readonly Option<OutputFormat> FormatOption = new("--format")
	{
		Description = "The output format. Defaults to text.",
	};

	/// <summary>
	/// Initializes a new instance of the <see cref="BranchPushesCommand"/> class.
	/// </summary>
	public BranchPushesCommand()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BranchPushesCommand"/> class.
	/// </summary>
	/// <param name="parseResult">The parse result.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	[SetsRequiredMembers]
	public BranchPushesCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.BranchName = parseResult.GetValue(BranchNameArgument)!;
		this.Top = parseResult.GetValue(TopOption);
		this.Before = parseResult.GetValue(BeforeOption);
		this.Format = parseResult.GetValue(FormatOption);
	}

	/// <summary>
	/// Gets the name of the branch to list pushes for.
	/// </summary>
	public required string BranchName { get; init; }

	/// <summary>
	/// Gets the maximum number of pushes to return.
	/// </summary>
	public int? Top { get; init; }

	/// <summary>
	/// Gets the timestamp before which pushes are returned.
	/// </summary>
	public DateTimeOffset? Before { get; init; }

	/// <summary>
	/// Gets the output format.
	/// </summary>
	public OutputFormat Format { get; init; }

	/// <summary>
	/// Creates the sub-command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static new Command CreateCommand()
	{
		Command command = new("pushes", "List recent pushes to a branch.")
		{
			BranchNameArgument,
			TopOption,
			BeforeOption,
			FormatOption,
		};
		AddCommonOptions(command);
		command.SetAction((parseResult, cancellationToken) => new BranchPushesCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteCoreAsync()
	{
		StringBuilder queryBuilder = new();
		queryBuilder.Append($"pushes?api-version=7.1&searchCriteria.refName={Uri.EscapeDataString(PrefixRef("refs/heads/", this.BranchName))}");

		queryBuilder.Append($"&$top={this.Top ?? DefaultTop}");

		if (this.Before is not null)
		{
			queryBuilder.Append($"&searchCriteria.toDate={Uri.EscapeDataString(this.Before.Value.UtcDateTime.ToString("O"))}");
		}

		HttpRequestMessage listRequest = new(HttpMethod.Get, queryBuilder.ToString());
		HttpResponseMessage? listResponse = await this.SendAsync(listRequest, canReadContent: false);
		if (!this.IsSuccessResponse(listResponse))
		{
			await this.PrintErrorMessageAsync(listResponse);
			return;
		}

		AzDOArray<GitPush>? pushList = await listResponse.Content.ReadFromJsonAsync(
			SourceGenerationContext.Default.AzDOArrayGitPush,
			this.CancellationToken);

		if (pushList is null || pushList.Count == 0)
		{
			if (this.Format == OutputFormat.Text)
			{
				this.Out.WriteLine("No pushes found.");
			}

			return;
		}

		// The list endpoint does not populate commits; fetch each push individually,
		// then resolve commits via the Commits endpoint (Pushes - Get commits array is unreliable).
		GitPush[] pushes = new GitPush[pushList.Value.Length];
		for (int i = 0; i < pushList.Value.Length; i++)
		{
			GitPush push = await this.GetPushAsync(pushList.Value[i].PushId) ?? pushList.Value[i];
			if (push.Commits is not { Length: > 0 })
			{
				push.Commits = await this.GetCommitsForPushAsync(push);
			}

			pushes[i] = push;
		}

		if (this.Format == OutputFormat.Json)
		{
			this.Out.WriteLine(JsonSerializer.Serialize(pushes, SourceGenerationContext.Default.GitPushArray));
			return;
		}

		foreach (GitPush push in pushes)
		{
			DateTimeOffset localTime = push.Date.ToLocalTime();
			string pusherName = push.PushedBy?.DisplayName ?? push.PushedBy?.UniqueName ?? "(unknown)";
			this.Out.WriteLine($"{localTime:yyyy-MM-dd HH:mm:ss} {pusherName}");

			if (push.Commits is { Length: > 0 })
			{
				foreach (GitCommitRef commit in push.Commits)
				{
					if (commit.CommitId is not { Length: > 0 } commitId)
					{
						continue;
					}

					string shortId = commitId.Length >= 16 ? commitId[..16] : commitId;
					string firstLine = commit.Comment?.Split('\n', 2)[0].Trim() ?? string.Empty;
					this.Out.WriteLine($"  {shortId} {firstLine}");
				}
			}
		}
	}

	/// <summary>
	/// Fetches a single push by its ID to retrieve full data including commits.
	/// </summary>
	/// <param name="pushId">The ID of the push to fetch.</param>
	/// <returns>The full push data, or <see langword="null"/> if the request failed.</returns>
	private async Task<GitPush?> GetPushAsync(int pushId)
	{
		HttpRequestMessage request = new(HttpMethod.Get, $"pushes/{pushId}?includeRefUpdates=true&$top={DefaultTop}&api-version=7.1");
		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: false);
		if (this.IsSuccessResponse(response))
		{
			return await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.GitPush, this.CancellationToken);
		}

		return null;
	}

	/// <summary>
	/// Fetches the tip commit for a push by looking up the <c>newObjectId</c> from the push's ref updates directly.
	/// This is the most reliable approach: the tip commit is always present and always includes the <c>comment</c> field.
	/// </summary>
	/// <param name="push">The push whose tip commit should be fetched.</param>
	/// <returns>A single-element array containing the tip commit, or <see langword="null"/> if not available.</returns>
	private async Task<GitCommitRef[]?> GetCommitsForPushAsync(GitPush push)
	{
		string refName = PrefixRef("refs/heads/", this.BranchName);
		GitRefUpdate? refUpdate = push.RefUpdates?.FirstOrDefault(
			r => string.Equals(r.Name, refName, StringComparison.OrdinalIgnoreCase));
		if (refUpdate?.NewObjectId is not { Length: > 0 } newOid)
		{
			return null;
		}

		HttpRequestMessage request = new(HttpMethod.Get, $"commits/{Uri.EscapeDataString(newOid)}?api-version=7.1");
		HttpResponseMessage? response = await this.SendAsync(request, canReadContent: false);
		if (this.IsSuccessResponse(response))
		{
			GitCommitRef? commit = await response.Content.ReadFromJsonAsync(
				SourceGenerationContext.Default.GitCommitRef, this.CancellationToken);
			return commit is null ? null : [commit];
		}

		return null;
	}
}
