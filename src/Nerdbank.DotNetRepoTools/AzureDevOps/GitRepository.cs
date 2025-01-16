// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Metadata about a Git repository.
/// </summary>
/// <remarks>
/// As described by <see href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/create?view=azure-devops-rest-7.1&amp;tabs=HTTP#gitrepository">the Azure DevOps REST API</see>.
/// </remarks>
internal class GitRepository
{
	public required string Url { get; set; }

	public required string WebUrl { get; set; }
}
