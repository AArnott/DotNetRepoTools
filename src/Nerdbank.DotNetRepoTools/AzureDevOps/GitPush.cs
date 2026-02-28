// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Represents a push to a Git repository.
/// </summary>
/// <remarks>
/// As described by <see href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pushes/list?view=azure-devops-rest-7.1#gitpush">the Azure DevOps REST API</see>.
/// </remarks>
internal class GitPush
{
	/// <summary>
	/// Gets or sets the unique identifier of this push.
	/// </summary>
	public int PushId { get; set; }

	/// <summary>
	/// Gets or sets the date and time when the push occurred (UTC).
	/// </summary>
	public DateTimeOffset Date { get; set; }

	/// <summary>
	/// Gets or sets the identity of the user who performed the push.
	/// </summary>
	public IdentityRef? PushedBy { get; set; }

	/// <summary>
	/// Gets or sets the commits included in this push. The API returns up to 100 commits per push.
	/// </summary>
	public GitCommitRef[]? Commits { get; set; }

	/// <summary>
	/// Gets or sets the ref updates included in this push.
	/// </summary>
	public GitRefUpdate[]? RefUpdates { get; set; }
}
