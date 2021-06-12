namespace PromoDesc
{
    using System;
    using System.Collections.Generic;
    using System.Data;
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

    using Microsoft.Extensions.Configuration;

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
        private const string SystemState = "/fields/System.State";
        private const string AddOperation = "add";
        private const string Task = "Task";
        public static char[] TagsDelimiters = new char[] { ' ', ',', ';' };

        private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PromoDesc.json");

        private static IConfigurationRoot Config;

        private static string Org => Config[nameof(Org)];

        private static string Project => Config[nameof(Project)];

        private static string Token => Config[nameof(Token)];

        private static string Pat => Convert.ToBase64String(Encoding.ASCII.GetBytes($":{Token ?? string.Empty}"));

        private static string DescriptionPrefix => Config[nameof(DescriptionPrefix)];

        private static string BaseUrl => $"https://dev.azure.com/{Org}/{Project}/_apis/wit";

        private static string RelationsQueryPath => $"{BaseUrl}/wiql?api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        private static string WorkItemsQueryPath => $"{BaseUrl}/workitems?ids={{0}}&api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        private static string WorkItemUpdateUrl => $"{BaseUrl}/workItems/{{0}}?api-version={ApiVersion}";

        private static string WorkItemsQuery => Config["WorkItemsQuery"];

        private static bool ReportOnly => bool.Parse(Config[nameof(ReportOnly)] ??  "false");

        private static List<EFU> efus = null;

        private static List<WorkItem> workitems = null;

        [STAThread]
        public static async Task Main(string[] args)
        {
            var builder = new ConfigurationBuilder();
            builder.AddJsonFile(GetSettingsFile(), false, true);
            Config = builder.Build();

            try
            {
                await ProcessWorksAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }

            ColorConsole.WriteLine($"\nDone! Press 'O' to open the file or any other key to exit...".White().OnGreen());
            var key = Console.ReadKey();
        }

        private static string GetSettingsFile()
        {
            if (!File.Exists(SettingsFile))
            {
                var settings = new { Org = "My Org", Project = "My Project", Token = "My PAT", DescriptionPrefix = "", WorkItemsQuery = "Select [System.Id], [System.WorkItemType], [System.Title], [System.Tags], [System.Description] From WorkItemLinks WHERE (Source.[System.TeamProject] = '{0}' and Source.[System.WorkItemType] = 'Epic') and ([System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward') and (Target.[System.State] != 'Removed' and Target.[System.WorkItemType] in ('User Story', 'Product Backlog Item', 'Task')) mode(Recursive)", ReportOnly = true };
                SetSettings(settings);
                ColorConsole.WriteLine("Update settings here: ".Red(), SettingsFile);
            }

            return SettingsFile;
        }

        private static void SetSettings(object settings)
        {
            File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore }));
        }

        private static void WriteError(string error)
        {
            ColorConsole.WriteLine($"Error: {error}".White().OnRed());
        }

        private static async Task ProcessWorksAsync()
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
                    await ProcessWorksAsync().ConfigureAwait(false);
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
                        State = wi.fields.SystemState,
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
            ColorConsole.Write($"\nProcess {grouping.Count()} PBIs? (Y/N) ".Yellow());
            var input = Console.ReadLine();
            if (input.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                await PromoteWorkItemDescriptionAsync(grouping).ConfigureAwait(false);
            }
        }

        private static async Task PromoteWorkItemDescriptionAsync(IEnumerable<IGrouping<int?, EFU>> grouping)
        {
            foreach (var group in grouping)
            {
                var parentId = group.Key;
                if (parentId != null)
                {
                    var childDescs = group.Where(x => x.Description != null).Select(x => "<li><b>" + x.Title + "</b><br />" + x.Description + "</li>")?.Distinct()?.ToList();
                    var childStates = group.Where(x => x.Workitemtype == "Task").Select(x => x.State)?.Distinct()?.ToList();
                    var parent = efus.SingleOrDefault(x => x.Id.Equals(parentId));

                    // Add if does not exist already
                    if (!parent.Description.Contains(DescriptionPrefix) && childDescs != null)
                    {
                        var desc = $"<br /><br /><b><u>{DescriptionPrefix}:</u></b><br /><ol type='1'>" + string.Join(string.Empty, childDescs) + "</ol>";
                        var state = parent.CalculateState(childStates);
                        await UpdateParentDescription(parent, desc, state).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task<bool> UpdateParentDescription(EFU parent, string desc, string state)
        {
            var update = false;
            if (parent != null && parent.Description?.Contains(desc) != true)
            {
                if (!ReportOnly)
                {
                    await SaveWorkItemsAsync(parent, desc, state).ConfigureAwait(false);
                }
                else
                {
                    ColorConsole.WriteLine($"Updating Description & State ({parent.State} > {state}) for {parent.Id}".Yellow());
                }
            }

            return update;
        }

        public static async Task SaveWorkItemsAsync(EFU workItem, string desc, string state)
        {
            var ops = new[]
            {
                new Op { op = AddOperation, path = SystemDescription, value = workItem.Description + desc },
                new Op { op = AddOperation, path = SystemState, value = state }
            }.ToList();

            var result = await ProcessRequest<dynamic>(string.Format(CultureInfo.InvariantCulture, WorkItemUpdateUrl, workItem.Id), ops?.ToJson(), true).ConfigureAwait(false);
            ColorConsole.WriteLine($"Updated Description & State ({workItem.State} > {state}) for {workItem.Id}".White().OnDarkGreen());
        }

        private static async Task<T> ProcessRequest<T>(string path, string content = null, bool patch = false)
        {
            try
            {
                // https://www.visualstudio.com/en-us/docs/integrate/api/wit/samples
                Trace.TraceInformation($"BaseAddress: {Org} | Path: {path} | Content: {content}");
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
