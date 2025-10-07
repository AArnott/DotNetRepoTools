// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// An CLI <see cref="Option{T}"/> that can be specified on the command line or via an environment variable.
/// </summary>
internal class OptionOrEnvVar : Option<string>
{
	public OptionOrEnvVar(string name, string envVar, bool isRequired = false, string? description = null, bool doNotAppendToDescription = false)
		: base(name)
	{
		this.EnvironmentVariableName = envVar;
		this.Description = description;
		if (!doNotAppendToDescription)
		{
			this.AppendDescription();
		}

		this.SetOtherProperties(isRequired);
	}

	public OptionOrEnvVar(string name, string[] aliases, string envVar, bool isRequired = false, string? description = null, bool doNotAppendToDescription = false)
		: base(name, aliases)
	{
		this.Description = description;
		this.EnvironmentVariableName = envVar;
		if (!doNotAppendToDescription)
		{
			this.AppendDescription();
		}

		this.SetOtherProperties(isRequired);
	}

	public string EnvironmentVariableName { get; private init; }

	public bool AppendToDescription { get; init; } = true;

	private void AppendDescription()
	{
		if (this.AppendToDescription)
		{
			this.Description += $" If not specified, the value of the {this.EnvironmentVariableName} environment variable will be used if set.";
		}
	}

	private void SetOtherProperties(bool isRequired)
	{
		if (Environment.GetEnvironmentVariable(this.EnvironmentVariableName) is { Length: > 0 } envVarValue)
		{
			this.DefaultValueFactory = argResult => envVarValue;
		}
		else
		{
			this.Required = isRequired;
		}
	}
}
