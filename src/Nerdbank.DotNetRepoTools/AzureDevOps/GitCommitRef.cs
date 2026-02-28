// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// A reference to a Git commit, as returned within push responses.
/// </summary>
/// <remarks>
/// As described by <see href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pushes/list?view=azure-devops-rest-7.1#gitcommitref">the Azure DevOps REST API</see>.
/// </remarks>
internal class GitCommitRef
{
	/// <summary>
	/// Gets or sets the full SHA-1 commit ID.
	/// </summary>
	public string? CommitId { get; set; }

	/// <summary>
	/// Gets or sets the commit comment (message). Only the first line is typically shown in summaries.
	/// </summary>
	public string? Comment { get; set; }
}
