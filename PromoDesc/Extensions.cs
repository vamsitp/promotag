namespace PromoDesc
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;

    using ColoredConsole;

    using Flurl.Http;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Newtonsoft.Json.Serialization;

    public static class Extensions
    {
        private const int Max = 200;

        public static string ToJson(this object source, Formatting formatting = Formatting.Indented, JsonSerializerSettings jsonSerializerSettings = null, bool camelizePropertyNames = false)
        {
            if (jsonSerializerSettings == null)
            {
                jsonSerializerSettings = new JsonSerializerSettings
                {
                    Error = (o, e) =>
                    {
                        var currentError = e.ErrorContext.Error.Message;
                        ColorConsole.WriteLine($"Error: {currentError}".White().OnRed());
                        e.ErrorContext.Handled = true;
                    },
                    ContractResolver = camelizePropertyNames ? new CamelCasePropertyNamesContractResolver() : new DefaultContractResolver()
                };
            }

            return source != null ? JsonConvert.SerializeObject(source, formatting, jsonSerializerSettings) : string.Empty;
        }

        // Credit: https://stackoverflow.com/a/11463800
        public static IEnumerable<List<T>> SplitList<T>(this List<T> list, int limit = Max)
        {
            if (list?.Any() == true)
            {
                for (var i = 0; i < list.Count; i += limit)
                {
                    yield return list.GetRange(i, Math.Min(limit, list.Count - i));
                }
            }
        }

        public static async Task<string> ToFullStringAsync(this Exception e, [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        {
            if (e != null)
            {
                var message = e.Message;
                var fex = e as FlurlHttpException;
                if (fex != null)
                {
                    try
                    {
                        var vex = await fex.GetResponseJsonAsync<JObject>().ConfigureAwait(false);
                        message = vex?.SelectToken("..message")?.Value<string>() ?? (vex?.SelectToken("..Message")?.Value<string>() ?? e.Message);
                    }
                    catch
                    {
                        // Do nothing
                    }
                }

                var lines = string.Join(" > ", new StackTrace(e, true)?.GetFrames()?.Select(x => x.GetFileLineNumber())?.Where(i => i > 0));
                return $"{message} (#{(string.IsNullOrWhiteSpace(lines) ? (member + "-" + line) : lines)})";
            }

            return string.Empty;
        }

        public static string CalculateState(this EFU parent, List<string> childStates)
        {
            if (parent.Workitemtype == "User Story") // US: New / Active / Resolved / Closed / Removed
            {
                // Task: New / Active / Closed / Removed
                if (childStates.All(x => x == "New"))
                {
                    return "New";
                }
                else if (childStates.All(x => x == "Removed"))
                {
                    return "Removed";
                }
                else if (childStates.All(x => x == "Closed"))
                {
                    return "Closed";
                }
                else if (childStates.All(x => x == "Closed" || x == "Removed"))
                {
                    return "Resolved";
                }
                else // Active
                {
                    return "Active";
                }
            }
            else if (parent.Workitemtype == "Product Backlog Item") // PBI: New / Approved / Committed / Done / Removed
            {
                // Task: ToDo / In Progress / Done / Removed
                if (childStates.All(x => x == "New"))
                {
                    return "New";
                }
                else if (childStates.All(x => x == "Removed"))
                {
                    return "Removed";
                }
                else if (childStates.All(x => x == "Done"))
                {
                    return "Done";
                }
                else if (childStates.All(x => x == "Done" || x == "Removed"))
                {
                    return "Approved";
                }
                else // In Progress
                {
                    return "Committed";
                }
            }

            return parent.State;
        }
    }
}
