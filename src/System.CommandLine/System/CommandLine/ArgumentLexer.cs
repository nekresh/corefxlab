﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace System.CommandLine
{
    internal static class ArgumentLexer
    {
        public static IReadOnlyList<ArgumentToken> Lex(IEnumerable<string> args, Func<string, IEnumerable<string>> responseFileReader = null)
        {
            var result = new List<ArgumentToken>();

            // We'll split the arguments into tokens.
            //
            // A token combines the modifier (/, -, --), the option name, and the option
            // value.
            // 
            // Please note that this code doesn't combine arguments. It only provides
            // some pre-processing over the arguments to split out the modifier,
            // option, and value:
            //
            // { "--out", "out.exe" } ==> { new ArgumentToken("--", "out", null),
            //                              new ArgumentToken(null, null, "out.exe") }
            //
            // {"--out:out.exe" }     ==> { new ArgumentToken("--", "out", "out.exe") }
            //
            // The reason it doesn't combine arguments is because it depends on the actual
            // definition. For example, if --out is a flag (meaning it's of type bool) then
            // out.exe in the first example wouldn't be considered its value.
            //
            // The code also handles the special -- token which indicates that the following
            // arguments shouldn't be considered options.
            //
            // Finally, this code will also expand any reponse file entries, assuming the caller
            // gave us a non-null reader.

            var hasSeenDashDash = false;

            foreach (var arg in ExpandResponseFiles(args, responseFileReader))
            {
                // If we've seen a -- already, then we'll treat one as a plain name, that is
                // without a modifier or value.

                if (!hasSeenDashDash && arg == @"--")
                {
                    hasSeenDashDash = true;
                    continue;
                }

                string modifier;
                string name;
                string value;

                if (hasSeenDashDash)
                {
                    modifier = null;
                    name = arg;
                    value = null;
                }
                else
                {
                    // If we haven't seen the -- separator, we're looking for options.
                    // Options have leading modifiers, i.e. /, -, or --.
                    //
                    // Options can also have values, such as:
                    //
                    //      -f:false
                    //      --name=hello

                    string nameAndValue;

                    if (!TryExtractOption(arg, out modifier, out nameAndValue))
                    {
                        name = arg;
                        value = null;
                    }
                    else if (!TrySplitNameValue(nameAndValue, out name, out value))
                    {
                        name = nameAndValue;
                        value = null;
                    }
                }

                var token = new ArgumentToken(modifier, name, value);
                result.Add(token);
            }

            // Single letter options can be combined, for example the following two
            // forms are considered equivalent:
            //
            //    (1)  -xdf
            //    (2)  -x -d -f
            //
            // In order to free later phases from handling this case, we simply expand
            // single letter options to the second form.

            for (var i = result.Count - 1; i >= 0; i--)
            {
                if (IsOptionBundle(result[i]))
                    ExpandOptionBundle(result, i);
            }

            return result.ToArray();
        }

        private static IEnumerable<string> ExpandResponseFiles(IEnumerable<string> args, Func<string, IEnumerable<string>> responseFileReader)
        {
            foreach (var arg in args)
            {
                if (responseFileReader == null || !arg.StartsWith(@"@"))
                {
                    yield return arg;
                }
                else
                {
                    var fileName = arg.Substring(1);

                    var responseFileArguments = responseFileReader(fileName);

                    // The reader can suppress expanding this response file by
                    // returning null. In that case, we'll treat the response
                    // file token as a regular argument.

                    if (responseFileArguments == null)
                    {
                        yield return arg;
                    }
                    else
                    {
                        foreach (var responseFileArgument in responseFileArguments)
                            yield return responseFileArgument.Trim();
                    }
                }
            }
        }

        private static bool IsOptionBundle(ArgumentToken token)
        {
            return token.IsOption &&
                   token.Modifier == @"-" &&
                   token.Name.Length > 1;
        }

        private static void ExpandOptionBundle(IList<ArgumentToken> receiver, int index)
        {
            var options = receiver[index].Name;
            receiver.RemoveAt(index);

            foreach (var c in options)
            {
                var name = char.ToString(c);
                var expandedToken = new ArgumentToken(@"-", name, null);
                receiver.Insert(index, expandedToken);
                index++;
            }
        }

        private static bool TryExtractOption(string text, out string modifier, out string remainder)
        {
            return TryExtractOption(text, @"--", out modifier, out remainder) ||
                   TryExtractOption(text, @"-", out modifier, out remainder);
        }

        private static bool TryExtractOption(string text, string prefix, out string modifier, out string remainder)
        {
            if (text.StartsWith(prefix))
            {
                remainder = text.Substring(prefix.Length);
                modifier = prefix;
                return true;
            }

            remainder = null;
            modifier = null;
            return false;
        }

        private static bool TrySplitNameValue(string text, out string name, out string value)
        {
            return TrySplitNameValue(text, ':', out name, out value) ||
                   TrySplitNameValue(text, '=', out name, out value);
        }

        private static bool TrySplitNameValue(string text, char separator, out string name, out string value)
        {
            var i = text.IndexOf(separator);
            if (i >= 0)
            {
                name = text.Substring(0, i);
                value = text.Substring(i + 1);
                return true;
            }

            name = null;
            value = null;
            return false;
        }
    }
}
