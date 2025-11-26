// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using NuGet.Commands.Restore;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.ProjectModel;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class TargetFrameworkAdapter(string targetFrameworks, NuGetFramework targetFramework, RestoreProjectAdapter project, IReadOnlyCollection<PackageReference> packages) : ITargetFramework
{
	private readonly string targetFrameworks = targetFrameworks;
	private readonly NuGetFramework targetFramework = targetFramework;
	private readonly RestoreProjectAdapter project = project;
	private readonly IReadOnlyCollection<PackageReference> packages = packages;
	private readonly string targetFrameworkString = targetFramework.GetShortFolderName();

	public IReadOnlyList<IItem> GetItems(string itemType)
	{
		return itemType switch
		{
			"PackageReference" => [.. this.packages.Select(i => new ItemAdapter(i))],
			_ => Array.Empty<IItem>(),
		};
	}

	public string GetProperty(string propertyName)
	{
		return propertyName switch
		{
			"MSBuildProjectName" or "PackageId" => Path.GetFileNameWithoutExtension(this.project.FullPath),
			"OriginalMSBuildStartupDirectory" or "MSBuildStartupDirectory" => Path.GetDirectoryName(this.project.FullPath)!,
			"PackageVersion" => "1.0.0",
			"RestoreOutputPath" => Path.GetTempPath(),
			"RestoreProjectStyle" => ProjectStyle.PackageReference.ToString(),
			"TargetFramework" => this.targetFrameworkString,
			"TargetFrameworkMoniker" => this.targetFramework.DotNetFrameworkName,
			"TargetFrameworks" => this.targetFrameworks,
			"UsingMicrosoftNETSdk" => bool.TrueString,
			_ => string.Empty,
		};
	}
}
