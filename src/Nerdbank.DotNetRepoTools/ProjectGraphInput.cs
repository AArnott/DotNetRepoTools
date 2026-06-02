// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Graph;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// The supported kinds of graph inputs.
/// </summary>
internal enum ProjectGraphInputKind
{
	/// <summary>
	/// A single project file.
	/// </summary>
	Project,

	/// <summary>
	/// A traditional Visual Studio solution file.
	/// </summary>
	Sln,

	/// <summary>
	/// A solution XML file.
	/// </summary>
	Slnx,
}

/// <summary>
/// The entry points and metadata used to build a project graph.
/// </summary>
/// <param name="InputKind">The kind of input file.</param>
/// <param name="EntryPoints">The graph entry points.</param>
/// <param name="ExplicitSolutionProjects">The projects explicitly listed in the solution, if any.</param>
internal sealed record ProjectGraphInput(
	ProjectGraphInputKind InputKind,
	IReadOnlyList<ProjectGraphEntryPoint> EntryPoints,
	HashSet<string> ExplicitSolutionProjects);
