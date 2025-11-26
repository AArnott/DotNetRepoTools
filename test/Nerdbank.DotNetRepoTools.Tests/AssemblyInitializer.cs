// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

internal static class AssemblyInitializer
{
	[ModuleInitializer]
	public static void Initialize()
	{
		MSBuild.MSBuildLocator.EnsureLoaded();
	}
}
