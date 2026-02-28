// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Describes a ref (branch) update that was part of a push.
/// </summary>
/// <remarks>
/// As described by <see href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pushes/get?view=azure-devops-rest-7.1#gitrefupdate">the Azure DevOps REST API</see>.
/// </remarks>
internal class GitRefUpdate
{
	/// <summary>
	/// Gets or sets the full ref name (e.g., <c>refs/heads/main</c>).
	/// </summary>
	public string? Name { get; set; }

	/// <summary>
	/// Gets or sets the commit ID the ref pointed to before the push.
	/// All zeros indicates a newly created ref.
	/// </summary>
	public string? OldObjectId { get; set; }

	/// <summary>
	/// Gets or sets the commit ID the ref points to after the push.
	/// </summary>
	public string? NewObjectId { get; set; }
}
