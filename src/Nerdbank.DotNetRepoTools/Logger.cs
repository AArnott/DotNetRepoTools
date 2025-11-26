// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using NuGet.Common;

namespace Nerdbank.DotNetRepoTools;

internal class Logger : ILogger
{
	public void Log(LogLevel level, string data)
	{
		Debug.WriteLine($"{level}: {data}");
	}

	public void Log(ILogMessage message)
	{
		this.Log(message.Level, message.Message);
	}

	public Task LogAsync(LogLevel level, string data)
	{
		this.Log(level, data);
		return Task.CompletedTask;
	}

	public Task LogAsync(ILogMessage message)
	{
		this.Log(message);
		return Task.CompletedTask;
	}

	public void LogDebug(string data)
	{
		this.Log(LogLevel.Debug, data);
	}

	public void LogError(string data)
	{
		this.Log(LogLevel.Error, data);
	}

	public void LogInformation(string data)
	{
		this.Log(LogLevel.Information, data);
	}

	public void LogInformationSummary(string data)
	{
		this.Log(LogLevel.Information, data);
	}

	public void LogMinimal(string data)
	{
		this.Log(LogLevel.Minimal, data);
	}

	public void LogVerbose(string data)
	{
		this.Log(LogLevel.Verbose, data);
	}

	public void LogWarning(string data)
	{
		this.Log(LogLevel.Warning, data);
	}
}
