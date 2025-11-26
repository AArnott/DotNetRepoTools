// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using NuGet.Commands.Restore;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class RestoreProjectAdapter : IProject
{
	private readonly Dictionary<string, ITargetFramework> targetFrameworks;

	public RestoreProjectAdapter(IReadOnlyCollection<PackageReference> packages, IReadOnlyList<NuGetFramework> targetFrameworks)
	{
		this.targetFrameworks = new Dictionary<string, ITargetFramework>(targetFrameworks.Count, StringComparer.OrdinalIgnoreCase);

		string targetFrameworksString = targetFrameworks.Count > 1 ? string.Join(";", targetFrameworks.Select(tf => tf.GetShortFolderName())) : string.Empty;

		foreach (NuGetFramework targetFramework in targetFrameworks)
		{
			this.targetFrameworks.Add(targetFramework.GetShortFolderName(), new TargetFrameworkAdapter(targetFrameworksString, targetFramework, this, packages));
		}

		this.OuterBuild = this.targetFrameworks.Values.First();
	}

	public required string FullPath { get; init; }

	public required string Directory { get; init; }

	public ITargetFramework OuterBuild { get; }

	public IReadOnlyDictionary<string, ITargetFramework> TargetFrameworks => this.targetFrameworks;
}
