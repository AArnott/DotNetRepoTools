// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// A reference to an Azure DevOps identity, as returned by REST API responses.
/// </summary>
/// <remarks>
/// As described by <see href="https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pushes/list?view=azure-devops-rest-7.1#identityref">the Azure DevOps REST API</see>.
/// </remarks>
internal class IdentityRef
{
	/// <summary>
	/// Gets or sets the display name of the identity.
	/// </summary>
	public string? DisplayName { get; set; }

	/// <summary>
	/// Gets or sets the unique name (e.g. email or domain\account) of the identity.
	/// </summary>
	public string? UniqueName { get; set; }

	/// <summary>
	/// Gets or sets the unique identifier of the identity.
	/// </summary>
	public string? Id { get; set; }
}
