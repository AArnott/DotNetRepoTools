// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft;
using Microsoft.Build.Evaluation;
using NuGet.Frameworks;

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// Resolves the target frameworks used by NuGet maintenance commands.
/// </summary>
internal static class TargetFrameworkResolver
{
	/// <summary>
	/// Resolves explicitly requested frameworks or discovers them from projects under a path.
	/// </summary>
	/// <param name="msbuild">The MSBuild instance used to evaluate projects.</param>
	/// <param name="path">The project file or directory to inspect.</param>
	/// <param name="requestedFrameworks">The explicitly requested frameworks, if any.</param>
	/// <returns>The distinct resolved frameworks.</returns>
	/// <exception cref="InvalidOperationException">Thrown when no target frameworks can be determined.</exception>
	internal static IReadOnlyList<NuGetFramework> Resolve(MSBuild msbuild, string path, IReadOnlyList<string>? requestedFrameworks)
	{
		Requires.NotNull(msbuild);
		Requires.NotNullOrEmpty(path);

		IEnumerable<string> frameworkNames = requestedFrameworks is { Count: > 0 }
			? requestedFrameworks
			: GetProjectPaths(path)
				.SelectMany(projectPath =>
				{
					Project project = msbuild.GetProject(projectPath, ProjectLoadSettings.IgnoreMissingImports);
					string targetFrameworks = project.GetPropertyValue("TargetFrameworks");
					return targetFrameworks.Length > 0 ? targetFrameworks.Split(';') : [project.GetPropertyValue("TargetFramework")];
				});

		HashSet<NuGetFramework> targetFrameworks = new(NuGetFramework.Comparer);
		foreach (string frameworkName in frameworkNames.SelectMany(framework => framework.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
		{
			targetFrameworks.Add(NuGetFramework.Parse(frameworkName));
		}

		if (targetFrameworks.Count == 0)
		{
			throw new InvalidOperationException($"Unable to determine target frameworks from '{path}'. Specify at least one with --framework.");
		}

		return [.. targetFrameworks];
	}

	private static IEnumerable<string> GetProjectPaths(string path)
	{
		return File.Exists(path)
			? [path]
			: Directory.EnumerateFiles(path, "*.*proj", SearchOption.AllDirectories);
	}
}
