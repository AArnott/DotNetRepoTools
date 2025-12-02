// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if NET10_0_OR_GREATER
using NuGet.Commands.Restore;
using NuGet.Packaging;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class ItemAdapter(PackageReference packageReference) : IItem
{
	private readonly string version = packageReference.AllowedVersions.ToString();

	public string Identity { get; init; } = packageReference.PackageIdentity.Id;

	public string GetMetadata(string name)
	{
		return name switch
		{
			"IsImplicitlyDefined" => bool.FalseString,
			"Version" => this.version,
			_ => string.Empty,
		};
	}
}
#endif
