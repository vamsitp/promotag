namespace PromoDesc
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using ColoredConsole;

    using Microsoft.Extensions.Configuration;

    using Newtonsoft.Json;

    public class Program
    {
        private const string Dot = ". ";
        private const string SystemDescription = "/fields/System.Description";
        private const string SystemState = "/fields/System.State";
        private const string AddOperation = "add";
        private const string Task = "Task";
        public static char[] TagsDelimiters = new char[] { ' ', ',', ';' };

        private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PromoDesc.json");

        private static Settings source;
        // private static Settings destination;

        private static List<EFU> efus = null;

        private static List<WorkItem> workitems = null;

        [STAThread]
        public static async Task Main(string[] args)
        {
            var builder = new ConfigurationBuilder();
            builder.AddJsonFile(GetSettingsFile(), false, true);
            var sourceConfig = builder.Build().GetSection("Source");
            source = new Settings
            {
                Org = sourceConfig["Org"],
                Project = sourceConfig["Project"],
                Token = sourceConfig["Token"],
                DescriptionPrefix = sourceConfig["DescriptionPrefix"],
                WorkItemsQuery = sourceConfig["WorkItemsQuery"],
                ReportOnly = bool.Parse(sourceConfig["ReportOnly"] ?? "false")
            };

            try
            {
                await ProcessWorksAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.Message.WriteError();
            }

            ColorConsole.WriteLine($"\nDone! Press 'O' to open the file or any other key to exit...".White().OnGreen());
            var key = Console.ReadKey();
        }

        private static string GetSettingsFile()
        {
            if (!File.Exists(SettingsFile))
{
                var settings = new { Source = new { Org = "Source Org", Project = "Source Project", Token = "Source PAT", DescriptionPrefix = "", WorkItemsQuery = "Select [System.Id], [System.WorkItemType], [System.Title], [System.Tags], [System.Description] From WorkItemLinks WHERE (Source.[System.TeamProject] = '{0}' and Source.[System.WorkItemType] = 'Epic') and ([System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward') and (Target.[System.State] != 'Removed' and Target.[System.WorkItemType] in ('User Story', 'Product Backlog Item', 'Task')) mode(Recursive)", ReportOnly = true } };
                SetSettings(settings);
                ColorConsole.WriteLine("Update settings here: ".Red(), SettingsFile);
            }

            return SettingsFile;
        }

        private static void SetSettings(object settings)
        {
            File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore }));
        }

        private static async Task ProcessWorksAsync()
        {
            if (efus == null)
            {
                efus = new List<EFU>();
                var wis = await AzDo.ProcessRequest<WiqlRelationList>(source, source.RelationsQueryPath, "{\"query\": \"" + string.Format(source.WorkItemsQuery, source.Project) + "\"}"); // AND [Source].[System.AreaPath] UNDER '{1}'
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
                workitems = await AzDo.GetWorkItems(source, relations.ToList());
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
                    if (!parent.Description.Contains(source.DescriptionPrefix) && childDescs != null)
                    {
                        var desc = $"<br /><br /><b><u>{source.DescriptionPrefix}:</u></b><br /><ol type='1'>" + string.Join(string.Empty, childDescs) + "</ol>";
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
                if (!source.ReportOnly)
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

        private static async Task SaveWorkItemsAsync(EFU workItem, string desc, string state)
        {
            var ops = new[]
            {
                new Op { op = AddOperation, path = SystemDescription, value = workItem.Description + desc },
                new Op { op = AddOperation, path = SystemState, value = state }
            }.ToList();

            var result = await AzDo.ProcessRequest<dynamic>(source, string.Format(CultureInfo.InvariantCulture, source.WorkItemUpdateUrl, workItem.Id), ops?.ToJson(), true).ConfigureAwait(false);
            ColorConsole.WriteLine($"Updated Description & State ({workItem.State} > {state}) for {workItem.Id}".White().OnDarkGreen());
        }
    }
}
