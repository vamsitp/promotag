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
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;

    using ColoredConsole;

    using Flurl.Http;
    using Flurl.Http.Content;

    using Newtonsoft.Json;

    public class Program
    {
        private const string RelationsQueryPath = "wit/wiql?api-version=" + ApiVersion; //"queries/Shared Queries/EFUs";
        private const string WorkItemsQueryPath = "wit/workitems?ids={0}&api-version=" + ApiVersion; //"queries/Shared Queries/EFUs";

        private static string Account => ConfigurationManager.AppSettings[nameof(Account)];
        private static string Project => ConfigurationManager.AppSettings["Project"];
        private static string Token => ConfigurationManager.AppSettings["PersonalAccessToken"];

        private static string BaseUrl => $"https://dev.azure.com/{Account}/{Project}/_apis/wit";

        private static string WorkItemUpdateUrl => $"{BaseUrl}/workItems/{{0}}?api-version={ApiVersion}";

        private static char[] TagsDelimiter = new char[] { ',', ';' };

        private static List<string> TagsToPromote => ConfigurationManager.AppSettings[nameof(TagsToPromote)].Split(TagsDelimiter, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();

        private static readonly string WorkItemsQuery = ConfigurationManager.AppSettings["WorkItemsQuery"];

        private const string WorkItemsJson = "./WorkItems.json";
        private const string Dot = ". ";
        private const string ApiVersion = "4.1-preview";
        private static List<EFU> efus = null;
        private static List<WorkItem> workitems = null;


        [STAThread]
        public static void Main(string[] args)
        {
            Execute(args).Wait();
            ColorConsole.WriteLine($"\nDone! Press any key to quit...".White().OnGreen());
            Console.ReadKey();
        }

        public static async Task Execute(string[] args)
        {
            try
            {
                CheckSettings();
                await ProcessWorkItems();
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        }

        private static void CheckSettings()
        {
            if (string.IsNullOrWhiteSpace(Account) || string.IsNullOrWhiteSpace(Project) || string.IsNullOrWhiteSpace(Token))
            {
                ColorConsole.WriteLine("\n Please provide Azure DevOps details in the format (without braces): <Account> <Project> <PersonalAccessToken>".Black().OnCyan());
                var details = Console.ReadLine().Split(' ')?.ToList();
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var section = config.Sections.OfType<AppSettingsSection>().FirstOrDefault();
                var settings = section.Settings;

                for (var i = 0; i < 3; i++) // Only 4 values required
                {
                    var key = settings.AllKeys[i];
                    settings[key].Value = details[i];
                }

                config.Save(ConfigurationSaveMode.Minimal);
                ConfigurationManager.RefreshSection(section.SectionInformation.Name);
            }
        }

        private static async Task ProcessWorkItems(bool local = false)
        {
            if (local)
            {
                efus = JsonConvert.DeserializeObject<List<EFU>>(File.ReadAllText(WorkItemsJson));
                ColorConsole.WriteLine($"Loaded {efus?.Count} Work-items from {WorkItemsJson}");
                await GetWorkItems();
            }
            else
            {
                await GetWorkItems();
            }
        }

        private static void WriteError(string error)
        {
            ColorConsole.WriteLine($"Error: {error}".White().OnRed());
        }

        private static async Task GetWorkItems()
        {
            // GetWorkItemsByQuery(workItems);
            await GetWorkItemsByStoredQuery(); //.ContinueWith(ContinuationAction);
        }

        private static async Task GetWorkItemsByStoredQuery()
        {
            if (efus == null)
            {
                efus = new List<EFU>();
                var wis = await GetData<WiqlRelationList>(RelationsQueryPath, Project, "{\"query\": \"" + string.Format(WorkItemsQuery, Project) + "\"}"); // AND [Source].[System.AreaPath] UNDER '{1}'
                ColorConsole.WriteLine($"Work-item relations fetched: {wis.workItemRelations.Length}");
                var rootItems = wis.workItemRelations.Where(x => x.source == null).ToList();
                await IterateWorkItems(rootItems, null, wis).ConfigureAwait(false);
                await SaveWorkItemsAsync().ConfigureAwait(false);
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
                        Tags = wi.fields.SystemTags?.Split(TagsDelimiter, StringSplitOptions.RemoveEmptyEntries)?.Select(x => x.Trim())?.ToList(),
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
                        var workItems = await GetData<WorkItems>(string.Format(WorkItemsQueryPath, ids), Project, string.Empty);
                        if (workItems != null)
                        {
                            result.AddRange(workItems.Items);
                        }
                    }
                }
            }

            return result;
        }

        private static async Task SaveWorkItemsAsync()
        {
            var grouping = efus.Where(x => x.Workitemtype.Equals("User Story") || x.Workitemtype.Equals("Product Backlog Item")).GroupBy(x => x.Parent); // .OrderByDescending(x => x.Id)
            await SaveWorkItemsAsync(grouping).ConfigureAwait(false);

            grouping = efus.Where(x => x.Workitemtype.Equals("Feature")).GroupBy(x => x.Parent);
            await SaveWorkItemsAsync(grouping).ConfigureAwait(false);
        }

        private static async Task SaveWorkItemsAsync(IEnumerable<IGrouping<int?, EFU>> grouping)
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

        public static async Task<bool> SaveWorkItemsAsync(EFU workItem)
        {
            var tags = string.Join(';', workItem.Tags);
            var pat = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{Token}"));
            var ops = new[]
                {
                    new Op { op = "add", path = "/fields/System.Tags", value = tags }
                }.ToList();

            var content = new CapturedStringContent(ops?.ToJson(), Encoding.UTF8, "application/json-patch+json");
            try
            {
                var result = await string.Format(CultureInfo.InvariantCulture, WorkItemUpdateUrl, workItem.Id)
                            .WithHeader("Authorization", "Basic " + pat)
                            .PatchAsync(content)
                            .ConfigureAwait(false);

                ColorConsole.WriteLine($"Updated Tags for {workItem.Id}: {tags}".White().OnDarkGreen());
                var success = result.StatusCode == System.Net.HttpStatusCode.OK;
                return success;
            }
            catch (Exception ex)
            {
                var err = await ex.ToFullStringAsync().ConfigureAwait(false);
                WriteError(err);
                return false;
            }
        }

        private static async Task<T> GetData<T>(string path, string project, string postData)
        {
            // https://www.visualstudio.com/en-us/docs/integrate/api/wit/samples
            using (var client = new HttpClient())
            {
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{Token}"));
                client.BaseAddress = new Uri(Account.IndexOf(".com", StringComparison.OrdinalIgnoreCase) > 0 ? Account : $"https://{Account}.visualstudio.com");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                if (!path.StartsWith(Account, StringComparison.OrdinalIgnoreCase))
                {
                    path = $"{project}/_apis/{path}";
                }

                Trace.TraceInformation($"BaseAddress: {Account} | Path: {path} | Content: {postData}");
                HttpResponseMessage queryHttpResponseMessage;

                if (string.IsNullOrWhiteSpace(postData))
                {
                    queryHttpResponseMessage = await client.GetAsync(path);
                }
                else
                {
                    var content = new StringContent(postData, Encoding.UTF8, "application/json");
                    queryHttpResponseMessage = await client.PostAsync(path, content);
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
        }

        private class Op
        {
            public string op { get; set; }

            public string path { get; set; }

            public dynamic value { get; set; }
        }
    }
}
