namespace PromoTagz
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;

    using ColoredConsole;

    using Flurl.Http;
    using Flurl.Http.Content;

    using Newtonsoft.Json;

    public class Program
    {
        private const string WorkItemsJson = "./WorkItems.json";
        private const string Dot = ". ";
        private const string ApiVersion = "4.1-preview";
        private const string JsonPatchMediaType = "application/json-patch+json";
        private const string JsonMediaType = "application/json";
        private const string AuthHeader = "Authorization";
        private const string BasicAuth = "Basic ";
        private const string SystemTags = "/fields/System.Tags";
        private const string AddOperation = "add";
        private const string UserStory = "User Story";
        private const string Pbi = "Product Backlog Item";
        private const string Feature = "Feature";

        private static string Account => ConfigurationManager.AppSettings[nameof(Account)];

        private static string Project => ConfigurationManager.AppSettings[nameof(Project)];

        private static string PersonalAccessToken => ConfigurationManager.AppSettings[nameof(PersonalAccessToken)];

        private static string Pat => Convert.ToBase64String(Encoding.ASCII.GetBytes($":{PersonalAccessToken ?? string.Empty}"));

        private static string BaseUrl => $"https://dev.azure.com/{Account}/{Project}/_apis/wit";

        private static string RelationsQueryPath = $"{BaseUrl}/wiql?api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        private static string WorkItemsQueryPath = $"{BaseUrl}/workitems?ids={{0}}&api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        private static string WorkItemUpdateUrl => $"{BaseUrl}/workItems/{{0}}?api-version={ApiVersion}";

        private static char[] TagsDelimiters = new char[] { ' ', ',', ';' };

        private static List<string> TagsToPromote => ConfigurationManager.AppSettings[nameof(TagsToPromote)].Split(TagsDelimiters, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();

        private static readonly string WorkItemsQuery = ConfigurationManager.AppSettings["WorkItemsQuery"];

        private static List<EFU> efus = null;

        private static List<WorkItem> workitems = null;

        [STAThread]
        public static void Main(string[] args)
        {
            Execute(args).Wait();
            ColorConsole.WriteLine($"\nDone! Press any key to quit...".White().OnGreen());
            Console.ReadKey();
        }

        public static async Task Execute(string[] tagsToPromote)
        {
            try
            {
                CheckSettings(tagsToPromote);
                await ProcessWorkItems(tagsToPromote);
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        }

        private static void CheckSettings(string[] tagsToPromote, bool reset = false)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var section = config.Sections.OfType<AppSettingsSection>().FirstOrDefault();
            var settings = section.Settings;
            var save = false;

            if (reset)
            {
                ConfigurationManager.AppSettings.Set(nameof(Account), string.Empty);
                ConfigurationManager.AppSettings.Set(nameof(Project), string.Empty);
                ConfigurationManager.AppSettings.Set(nameof(PersonalAccessToken), string.Empty);
            }

            if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Project) || string.IsNullOrWhiteSpace(PersonalAccessToken))
            {
                ColorConsole.WriteLine("\n Please provide Azure DevOps details in the format (without braces): <Account> <Project> <PersonalAccessToken>".Black().OnCyan());
                var details = Console.ReadLine().Split(' ')?.ToList();

                for (var i = 0; i < 3; i++) // Only 4 values required
                {
                    var key = settings.AllKeys[i];
                    settings[key].Value = details[i];
                }

                save = true;
            }

            if (tagsToPromote?.Length > 0)
            {
                var value = string.Join(' ', tagsToPromote);
                var prevValue = settings[nameof(TagsToPromote)].Value;
                if (!value.Trim().Equals(prevValue.Trim()))
                {
                    ColorConsole.WriteLine($"\n Overwriting TagsToPromote: '{value}' (Previous: '{settings[nameof(TagsToPromote)].Value}')".Black().OnCyan());
                    settings[nameof(TagsToPromote)].Value = value;
                    save = true;
                }
            }
            else
            {
                if (TagsToPromote?.Count() == 0)
                {
                    ColorConsole.WriteLine("\n No previous Tags found! Please provide the Tags to promote (space-separated)".Black().OnCyan());
                }
            }

            if (save)
            {
                config.Save(ConfigurationSaveMode.Minimal);
                ConfigurationManager.RefreshSection(section.SectionInformation.Name);
            }
        }

        private static async Task ProcessWorkItems(string[] tagsToPromote, bool local = false)
        {
            if (local)
            {
                efus = JsonConvert.DeserializeObject<List<EFU>>(File.ReadAllText(WorkItemsJson));
                ColorConsole.WriteLine($"Loaded {efus?.Count} Work-items from {WorkItemsJson}");
            }

            await ProcessWorkItemTagsAsync(tagsToPromote).ConfigureAwait(false);
        }

        private static void WriteError(string error)
        {
            ColorConsole.WriteLine($"Error: {error}".White().OnRed());
        }

        private static async Task ProcessWorkItemTagsAsync(string[] tagsToPromote)
        {
            if (efus == null)
            {
                efus = new List<EFU>();
                var wis = await ProcessRequest<WiqlRelationList>(RelationsQueryPath, "{\"query\": \"" + string.Format(WorkItemsQuery, Project) + "\"}"); // AND [Source].[System.AreaPath] UNDER '{1}'
                if (wis != null)
                {
                    ColorConsole.WriteLine($"Work-item relations fetched: {wis.workItemRelations.Length}");
                    var rootItems = wis.workItemRelations.Where(x => x.source == null).ToList();
                    await IterateWorkItems(rootItems, null, wis).ConfigureAwait(false);
                    await PromoteWorkItemTagsAsync().ConfigureAwait(false);
                }
                else
                {
                    CheckSettings(tagsToPromote, true);
                    await ProcessWorkItemTagsAsync(tagsToPromote).ConfigureAwait(false);
                }
            }
        }

        private static async Task IterateWorkItems(List<WorkitemRelation> relations, EFU parent, WiqlRelationList wis)
        {
            if (relations.Count > 0)
            {
                workitems = await GetWorkItems(relations.ToList());
                foreach (var wi in workitems)
                {
                    ColorConsole.WriteLine($" {wi.fields.SystemWorkItemType} ".PadRight(13) + wi.id.ToString().PadLeft(4) + Dot + wi.fields.SystemTitle + $" [{wi.fields.SystemTags}]");
                    var efu = new EFU
                    {
                        Id = wi.id,
                        Title = wi.fields.SystemTitle,
                        Workitemtype = wi.fields.SystemWorkItemType,
                        Tags = wi.fields.SystemTags?.Split(TagsDelimiters, StringSplitOptions.RemoveEmptyEntries)?.Select(x => x.Trim())?.ToList(),
                        Parent = parent?.Id
                    };

                    efus.Add(efu);
                    parent?.Children.Add(efu.Id);
                    await IterateWorkItems(wis.workItemRelations.Where(x => x.source != null && x.source.id.Equals(wi.id)).ToList(), efu, wis);
                }
            }
        }

        private static async Task<List<WorkItem>> GetWorkItems(List<WorkitemRelation> items)
        {
            var result = new List<WorkItem>();
            var splitItems = items.SplitList();
            if (splitItems?.Any() == true)
            {
                foreach (var relations in splitItems)
                {
                    var builder = new StringBuilder();
                    foreach (var item in relations.Select(x => x.target))
                    {
                        builder.Append(item.id.ToString()).Append(',');
                    }

                    var ids = builder.ToString().TrimEnd(',');
                    if (!string.IsNullOrWhiteSpace(ids))
                    {
                        var workItems = await ProcessRequest<WorkItems>(string.Format(WorkItemsQueryPath, ids));
                        if (workItems != null)
                        {
                            result.AddRange(workItems.Items);
                        }
                    }
                }
            }

            return result;
        }

        private static async Task PromoteWorkItemTagsAsync()
        {
            var grouping = efus.Where(x => x.Workitemtype.Equals(UserStory) || x.Workitemtype.Equals(Pbi)).GroupBy(x => x.Parent); // .OrderByDescending(x => x.Id)
            await PromoteWorkItemTagsAsync(grouping).ConfigureAwait(false);

            grouping = efus.Where(x => x.Workitemtype.Equals(Feature)).GroupBy(x => x.Parent);
            await PromoteWorkItemTagsAsync(grouping).ConfigureAwait(false);
        }

        private static async Task PromoteWorkItemTagsAsync(IEnumerable<IGrouping<int?, EFU>> grouping)
        {
            foreach (var group in grouping)
            {
                var parentId = group.Key;
                if (parentId != null)
                {
                    var tags = group.Where(x => x.Tags != null).SelectMany(x => x.Tags)?.Distinct()?.ToList();
                    var parent = efus.SingleOrDefault(x => x.Id.Equals(parentId));

                    // Add
                    if (tags != null)
                    {
                        foreach (var tagToPromote in tags?.Where(x => TagsToPromote.Contains(x, StringComparer.OrdinalIgnoreCase)))
                        {
                            await UpdateParentTags(parent, tagToPromote, false).ConfigureAwait(false);
                        }
                    }

                    // Remove
                    foreach (var tagToPromote in TagsToPromote.Where(x => !(tags?.Contains(x, StringComparer.OrdinalIgnoreCase) == true)))
                    {
                        await UpdateParentTags(parent, tagToPromote, true).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task<bool> UpdateParentTags(EFU parent, string tagToPromote, bool remove)
        {
            var update = false;
            if (parent != null)
            {
                var contains = parent.Tags?.Contains(tagToPromote) == true;
                if (remove)
                {
                    if (contains)
                    {
                        if (parent.Tags == null)
                        {
                            parent.Tags = new List<string>();
                        }

                        parent.Tags.Remove(tagToPromote);
                        update = true;
                    }
                }
                else
                {
                    if (!contains)
                    {
                        if (parent.Tags == null)
                        {
                            parent.Tags = new List<string>();
                        }

                        parent.Tags.Add(tagToPromote);
                        update = true;
                    }
                }

                if (update)
                {
                    await SaveWorkItemsAsync(parent).ConfigureAwait(false);
                }
            }

            return update;
        }

        public static async Task SaveWorkItemsAsync(EFU workItem)
        {
            var tags = string.Join(';', workItem.Tags);
            var ops = new[]
                {
                    new Op { op = AddOperation, path = SystemTags, value = tags }
                }.ToList();

            var result = await ProcessRequest<dynamic>(string.Format(CultureInfo.InvariantCulture, WorkItemUpdateUrl, workItem.Id), ops?.ToJson(), true).ConfigureAwait(false);
            ColorConsole.WriteLine($"Updated Tags for {workItem.Id}: {tags}".White().OnDarkGreen());
        }

        private static async Task<T> ProcessRequest<T>(string path, string content = null, bool patch = false)
        {
            try
            {
                // https://www.visualstudio.com/en-us/docs/integrate/api/wit/samples
                Trace.TraceInformation($"BaseAddress: {Account} | Path: {path} | Content: {content}");
                HttpResponseMessage queryHttpResponseMessage;
                var request = path.WithHeader(AuthHeader, BasicAuth + Pat);

                if (string.IsNullOrWhiteSpace(content))
                {
                    queryHttpResponseMessage = await request.GetAsync().ConfigureAwait(false);
                }
                else
                {
                    if (patch)
                    {
                        var stringContent = new CapturedStringContent(content, Encoding.UTF8, JsonPatchMediaType);
                        queryHttpResponseMessage = await request.PatchAsync(stringContent).ConfigureAwait(false);
                    }
                    else
                    {
                        var stringContent = new StringContent(content, Encoding.UTF8, JsonMediaType);
                        queryHttpResponseMessage = await request.PostAsync(stringContent).ConfigureAwait(false);
                    }
                }

                if (queryHttpResponseMessage.IsSuccessStatusCode)
                {
                    var result = await queryHttpResponseMessage.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<T>(result);
                }
                else
                {
                    throw new Exception($"{queryHttpResponseMessage.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                var err = await ex.ToFullStringAsync().ConfigureAwait(false);
                WriteError(err);
                return default(T);
            }
        }

        private class Op
        {
            public string op { get; set; }

            public string path { get; set; }

            public dynamic value { get; set; }
        }
    }
}
