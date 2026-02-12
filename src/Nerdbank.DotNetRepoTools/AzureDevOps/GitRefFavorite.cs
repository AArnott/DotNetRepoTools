// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Represents a Git ref favorite.
/// </summary>
/// <remarks>
/// As described by <see href="https://learn.microsoft.com/en-us/dotnet/api/microsoft.teamfoundation.sourcecontrol.webapi.gitreffavorite">the Azure DevOps REST API</see>.
/// </remarks>
internal class GitRefFavorite
{
	/// <summary>
	/// Gets or sets the ID of the favorite.
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// Gets or sets the name of the ref (e.g., "refs/heads/main").
	/// </summary>
	public required string Name { get; set; }

	/// <summary>
	/// Gets or sets the repository ID as a GUID.
	/// </summary>
	public required Guid RepositoryId { get; set; }

	/// <summary>
	/// Gets or sets the type of favorite.
	/// </summary>
	public required string Type { get; set; }

	/// <summary>
	/// Gets or sets the URL of the favorite.
	/// </summary>
	public string? Url { get; set; }
}
