// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// An CLI <see cref="Option{T}"/> that can be specified on the command line or via an environment variable.
/// </summary>
internal class OptionOrEnvVar : Option<string>
{
	public OptionOrEnvVar(string name, string envVar, bool isRequired = false, string? description = null)
		: base(name, description)
	{
		this.EnvironmentVariableName = envVar;
		this.AppendDescription(isRequired);
	}

	public OptionOrEnvVar(string[] aliases, string envVar, bool isRequired = false, string? description = null)
		: base(aliases, description)
	{
		this.EnvironmentVariableName = envVar;
		this.AppendDescription(isRequired);
	}

	public string EnvironmentVariableName { get; private init; }

	private void AppendDescription(bool isRequired)
	{
		this.Description += $" If not specified, the value of the {this.EnvironmentVariableName} environment variable will be used if set.";
		if (Environment.GetEnvironmentVariable(this.EnvironmentVariableName) is { Length: > 0 } envVarValue)
		{
			this.SetDefaultValue(envVarValue);
		}
		else
		{
			this.IsRequired = isRequired;
		}
	}
}
