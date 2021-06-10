namespace PromoDesc
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Configuration;
    using System.Data;
    using System.Diagnostics;
    using System.Globalization;
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
        private const string Dot = ". ";
        private const string ApiVersion = "5.1";
        private const string JsonPatchMediaType = "application/json-patch+json";
        private const string JsonMediaType = "application/json";
        private const string AuthHeader = "Authorization";
        private const string BasicAuth = "Basic ";
        private const string SystemDescription = "/fields/System.Description";
        private const string AddOperation = "add";
        private const string Task = "Task";
        public static char[] TagsDelimiters = new char[] { ' ', ',', ';' };

        private static string Account => ConfigurationManager.AppSettings[nameof(Account)];

        private static string Project => ConfigurationManager.AppSettings[nameof(Project)];

        private static string PersonalAccessToken => ConfigurationManager.AppSettings[nameof(PersonalAccessToken)];

        private static string Pat => Convert.ToBase64String(Encoding.ASCII.GetBytes($":{PersonalAccessToken ?? string.Empty}"));

        private static string BaseUrl => $"https://dev.azure.com/{Account}/{Project}/_apis/wit";

        private static string RelationsQueryPath = $"{BaseUrl}/wiql?api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        private static string WorkItemsQueryPath = $"{BaseUrl}/workitems?ids={{0}}&api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        private static string WorkItemUpdateUrl => $"{BaseUrl}/workItems/{{0}}?api-version={ApiVersion}";

        private static readonly string WorkItemsQuery = ConfigurationManager.AppSettings["WorkItemsQuery"];

        private static bool ReportOnly => ConfigurationManager.AppSettings.AllKeys.Contains(nameof(ReportOnly)) && bool.Parse(ConfigurationManager.AppSettings[nameof(ReportOnly)]);

        private static List<EFU> efus = null;

        private static List<WorkItem> workitems = null;

        [STAThread]
        public static void Main(string[] args)
        {
            Execute().Wait();
            ColorConsole.WriteLine($"\nDone! Press 'O' to open the file or any other key to exit...".White().OnGreen());
            var key = Console.ReadKey();
        }

        public static async Task Execute()
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

        private static void CheckSettings(bool reset = false)
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

            if (save)
            {
                config.Save(ConfigurationSaveMode.Minimal);
                ConfigurationManager.RefreshSection(section.SectionInformation.Name);
            }
        }

        private static async Task ProcessWorkItems()
        {
            await ProcessWorkItemDescAsync().ConfigureAwait(false);
        }

        private static void WriteError(string error)
        {
            ColorConsole.WriteLine($"Error: {error}".White().OnRed());
        }

        private static async Task ProcessWorkItemDescAsync()
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
                    await PromoteWorkItemDescriptionAsync().ConfigureAwait(false);
                }
                else
                {
                    CheckSettings(true);
                    await ProcessWorkItemDescAsync().ConfigureAwait(false);
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
                    ColorConsole.WriteLine($" {wi.fields.SystemWorkItemType} ".PadLeft(22) + wi.id.ToString().PadLeft(6) + Dot + wi.fields.SystemTitle);
                    var efu = new EFU
                    {
                        Id = wi.id,
                        Title = wi.fields.SystemTitle,
                        Description = wi.fields.SystemDescription,
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

        private static async Task PromoteWorkItemDescriptionAsync()
        {
            var grouping = efus.Where(x => x.Workitemtype.Equals(Task)).GroupBy(x => x.Parent); // .OrderByDescending(x => x.Id)
            await PromoteWorkItemDescriptionAsync(grouping).ConfigureAwait(false);
        }

        private static async Task PromoteWorkItemDescriptionAsync(IEnumerable<IGrouping<int?, EFU>> grouping)
        {
            foreach (var group in grouping)
            {
                var parentId = group.Key;
                if (parentId != null)
                {
                    var descs = group.Where(x => x.Description != null).Select(x => "<li><b>" + x.Title + "</b><br />" + x.Description + "</li>")?.Distinct()?.ToList();
                    var parent = efus.SingleOrDefault(x => x.Id.Equals(parentId));

                    // Add
                    if (descs != null)
                    {
                        var desc = "<br /><b><u>ACAI:</u></b><br /><ol type='1'>" + string.Join(string.Empty, descs) + "</ol>";
                        await UpdateParentDescription(parent, desc).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task<bool> UpdateParentDescription(EFU parent, string desc)
        {
            var update = false;
            if (parent != null && parent.Description?.Contains(desc) != true)
            {
                ColorConsole.WriteLine($" Description '{desc}' promoted to parent '{parent.Id}'".Yellow());
                if (!ReportOnly)
                {
                    await SaveWorkItemsAsync(parent, desc).ConfigureAwait(false);
                }
            }

            return update;
        }

        public static async Task SaveWorkItemsAsync(EFU workItem, string desc)
        {
            var ops = new[]
            {
                new Op { op = AddOperation, path = SystemDescription, value = workItem.Description + desc }
            }.ToList();

            var result = await ProcessRequest<dynamic>(string.Format(CultureInfo.InvariantCulture, WorkItemUpdateUrl, workItem.Id), ops?.ToJson(), true).ConfigureAwait(false);
            ColorConsole.WriteLine($"Updated Description for {workItem.Id}: {desc}".White().OnDarkGreen());
        }

        private static async Task<T> ProcessRequest<T>(string path, string content = null, bool patch = false)
        {
            try
            {
                // https://www.visualstudio.com/en-us/docs/integrate/api/wit/samples
                Trace.TraceInformation($"BaseAddress: {Account} | Path: {path} | Content: {content}");
                IFlurlResponse queryHttpResponseMessage;
                var request = path.WithHeader(AuthHeader, BasicAuth + Pat);

                if (string.IsNullOrWhiteSpace(content))
                {
                    queryHttpResponseMessage = await request.GetAsync().ConfigureAwait(false);
                }
                else
                {
                    if (patch)
                    {
                        var stringContent = new CapturedStringContent(content, JsonPatchMediaType);
                        queryHttpResponseMessage = await request.PatchAsync(stringContent).ConfigureAwait(false);
                    }
                    else
                    {
                        var stringContent = new StringContent(content, Encoding.UTF8, JsonMediaType);
                        queryHttpResponseMessage = await request.PostAsync(stringContent).ConfigureAwait(false);
                    }
                }

                if (queryHttpResponseMessage.ResponseMessage.IsSuccessStatusCode)
                {
                    var result = await queryHttpResponseMessage.ResponseMessage.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<T>(result);
                }
                else
                {
                    throw new Exception($"{queryHttpResponseMessage.ResponseMessage.ReasonPhrase}");
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
