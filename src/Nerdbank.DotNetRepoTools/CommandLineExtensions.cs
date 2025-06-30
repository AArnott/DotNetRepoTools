// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools;

internal static class CommandLineExtensions
{
	internal static bool HasAlias<T>(this Option<T> option, string alias) => option.Name == alias || option.Aliases.Contains(alias);
}
