// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using NuGet.Commands.Restore;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class RestoreProjectAdapter : IProject
{
	private readonly Project project;

	public RestoreProjectAdapter(Project project)
	{
		this.project = project;
		this.OuterBuild = new TargetFrameworkAdapter(project.CreateProjectInstance());

		Dictionary<string, ITargetFramework> frameworks = new(StringComparer.OrdinalIgnoreCase);
		if (project.GetPropertyValue("TargetFramework") is { Length: > 0 } targetFramework)
		{
			frameworks.Add(targetFramework, this.OuterBuild);
		}
		else if (project.GetPropertyValue("TargetFrameworks") is { Length: > 0 } targetFrameworks)
		{
			foreach (string tf in targetFrameworks.Split(';'))
			{
				string tfTrimmed = tf.Trim();
				Dictionary<string, string> globalProperties = project.GlobalProperties.ToDictionary();
				globalProperties["TargetFramework"] = tfTrimmed;
				ProjectInstance tfInstance = new(project.Xml, globalProperties, null, project.ProjectCollection);
				frameworks.Add(tfTrimmed, new TargetFrameworkAdapter(tfInstance));
			}
		}

		this.TargetFrameworks = frameworks;
	}

	public string FullPath => this.project.FullPath;

	public string Directory => this.project.DirectoryPath;

	public ITargetFramework OuterBuild { get; }

	public IReadOnlyDictionary<string, ITargetFramework> TargetFrameworks { get; }
}
