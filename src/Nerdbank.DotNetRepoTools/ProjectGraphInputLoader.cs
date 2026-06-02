// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Graph;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Nerdbank.DotNetRepoTools;

internal static class ProjectGraphInputLoader
{
	/// <summary>
	/// Checks whether the file extension is supported as a graph input.
	/// </summary>
	/// <param name="extension">The file extension to inspect.</param>
	/// <returns><see langword="true"/> if the extension is supported; otherwise <see langword="false"/>.</returns>
	internal static bool IsSupportedInput(string extension)
		=> extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
		|| extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
		|| extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Loads the graph entry points for a project or solution input.
	/// </summary>
	/// <param name="inputPath">The input project or solution path.</param>
	/// <param name="isExcludedProjectPath">A predicate that identifies excluded projects.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The loaded graph input.</returns>
	internal static async Task<ProjectGraphInput> LoadAsync(string inputPath, Func<string, bool> isExcludedProjectPath, CancellationToken cancellationToken)
	{
		string extension = Path.GetExtension(inputPath);
		if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
			&& !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
		{
			return new ProjectGraphInput(
				ProjectGraphInputKind.Project,
				isExcludedProjectPath(inputPath) ? [] : [new ProjectGraphEntryPoint(inputPath)],
				new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		ISolutionSerializer serializer = SolutionSerializers.GetSerializerByMoniker(inputPath) ?? throw new InvalidOperationException($"No solution serializer is available for '{inputPath}'.");
		SolutionModel solutionModel = await serializer.OpenAsync(inputPath, cancellationToken);
		string solutionDirectory = Path.GetDirectoryName(inputPath)!;

		HashSet<string> explicitProjects = new(StringComparer.OrdinalIgnoreCase);
		List<ProjectGraphEntryPoint> entryPoints = [];
		foreach (SolutionProjectModel solutionProject in solutionModel.SolutionProjects)
		{
			string projectPath = NormalizePathRelativeTo(solutionDirectory, solutionProject.FilePath);
			if (isExcludedProjectPath(projectPath))
			{
				continue;
			}

			explicitProjects.Add(projectPath);
			entryPoints.Add(new ProjectGraphEntryPoint(projectPath));
		}

		return new ProjectGraphInput(
			extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? ProjectGraphInputKind.Slnx : ProjectGraphInputKind.Sln,
			entryPoints,
			explicitProjects);
	}

	private static string NormalizePathRelativeTo(string baseDirectory, string path)
		=> NormalizePath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

	private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
