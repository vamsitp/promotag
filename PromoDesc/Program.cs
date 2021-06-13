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
        private static Settings target;

        private static Dictionary<string, List<EFU>> efus = new Dictionary<string, List<EFU>> { { nameof(source), null }, { nameof(target), null } };

        private static List<WorkItem> workitems = null;

        [STAThread]
        public static async Task Main(string[] args)
        {
            PrintHelp();
            int.TryParse(Console.ReadLine(), out var choice);

            var builder = new ConfigurationBuilder();
            builder.AddJsonFile(GetSettingsFile(), false, true);
            var sourceConfig = builder.Build().GetSection("Source");
            source = new Settings
            {
                Name = nameof(source),
                Org = sourceConfig["Org"],
                Project = sourceConfig["Project"],
                Token = sourceConfig["Token"],
                DescriptionPrefix = sourceConfig["DescriptionPrefix"],
                WorkItemsQuery = sourceConfig["WorkItemsQuery"],
                ReportOnly = bool.Parse(sourceConfig["ReportOnly"] ?? "false")
            };

            var targetConfig = builder.Build().GetSection("Target");
            target = new Settings
            {
                Name = nameof(target),
                Org = targetConfig["Org"],
                Project = targetConfig["Project"],
                Token = targetConfig["Token"],
                DescriptionPrefix = targetConfig["DescriptionPrefix"],
                WorkItemsQuery = targetConfig["WorkItemsQuery"],
                ReportOnly = bool.Parse(targetConfig["ReportOnly"] ?? "false")
            };

            try
            {
                if (choice == 1)
                {
                    await ProcessWorkItemsAsync(target).ConfigureAwait(false);
                    await PromoteChildDescriptionsToParentAsync(target).ConfigureAwait(false);
                }
                else
                {
                    await ProcessWorkItemsAsync(source).ConfigureAwait(false);
                    await ProcessWorkItemsAsync(target).ConfigureAwait(false);

                    if (choice == 2)
                    {
                        await ImportSourcePbisIntoTarget().ConfigureAwait(false);
                    }
                    else if (choice == 3)
                    {
                        await MoveSourceTasksToTargetUserStories().ConfigureAwait(false);
                    }
                    else if (choice == 4)
                    {
                        await UpdateTargetUserStoryFieldsFromSource().ConfigureAwait(false);
                    }
                    else if (choice == 5)
                    {
                        await AppendSourceUserStoryDescriptionToTarget().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Message.WriteError();
            }

            ColorConsole.WriteLine($"\nDone! Press 'O' to open the file or any other key to exit...".White().OnGreen());
            var key = Console.ReadKey();
        }

        private static void PrintHelp()
        {
            ColorConsole.WriteLine("1. Populate parent User-story Description from child Tasks in the Target".Yellow());
            ColorConsole.WriteLine("2. Import Source PBIs into Target (EFUT)".Yellow());
            ColorConsole.WriteLine("3. Move Source Tasks to Target User-stories (based on Source Task Title match in Target User-story Description)".Yellow());
            ColorConsole.WriteLine("4. Update Target User-story Description, Tags, Iteration-Path from Source User-Stories (based on Title match)".Yellow());
            ColorConsole.WriteLine("5. Append Source User-story Description with Target User-story with Source Description-prefix (based on Title match)".Yellow());
            ColorConsole.Write("\nChoose option (1 / 2 / 3 / 4 / 5): ");
        }

        private static string GetSettingsFile()
        {
            if (!File.Exists(SettingsFile))
{
                var settings = new { Source = new { Org = "Source Org", Project = "Source Project", Token = "Source PAT", DescriptionPrefix = "", WorkItemsQuery = "Select [System.Id], [System.WorkItemType], [System.Title], [System.Tags], [System.Description] From WorkItemLinks WHERE (Source.[System.TeamProject] = '{0}' and Source.[System.WorkItemType] = 'Epic') and ([System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward') and (Target.[System.State] != 'Removed' and Target.[System.WorkItemType] in ('User Story', 'Product Backlog Item', 'Task')) mode(Recursive)", ReportOnly = true }, Target = new { Org = "Target Org", Project = "Target Project", Token = "Target PAT", DescriptionPrefix = "", WorkItemsQuery = "Select [System.Id], [System.WorkItemType], [System.Title], [System.Tags], [System.Description] From WorkItemLinks WHERE (Source.[System.TeamProject] = '{0}' and Source.[System.WorkItemType] = 'Epic') and ([System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward') and (Target.[System.State] != 'Removed' and Target.[System.WorkItemType] in ('User Story', 'Product Backlog Item', 'Task')) mode(Recursive)", ReportOnly = true } };
                SetSettings(settings);
                ColorConsole.WriteLine("Update settings here: ".Red(), SettingsFile);
            }

            return SettingsFile;
        }

        private static void SetSettings(object settings)
        {
            File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore }));
        }

        private static async Task ProcessWorkItemsAsync(Settings account)
        {
            if (!string.IsNullOrWhiteSpace(account.Org) && !string.IsNullOrWhiteSpace(account.Project) && !string.IsNullOrWhiteSpace(account.Token) && !string.IsNullOrWhiteSpace(account.WorkItemsQuery) && efus[account.Name] == null)
            {
                efus[account.Name] = new List<EFU>();
                var wis = await AzDo.ProcessRequest<WiqlRelationList>(account, account.RelationsQueryPath, "{\"query\": \"" + string.Format(account.WorkItemsQuery, account.Project) + "\"}"); // AND [Source].[System.AreaPath] UNDER '{1}'
                if (wis != null)
                {
                    ColorConsole.WriteLine($"Work-item relations fetched: {wis.workItemRelations.Length}");
                    var rootItems = wis.workItemRelations.Where(x => x.source == null).ToList();
                    await IterateWorkItems(rootItems, null, wis, account).ConfigureAwait(false);
                }
                else
                {
                    await ProcessWorkItemsAsync(account).ConfigureAwait(false);
                }
            }
            else
            {
                throw new Exception($"Fill the missing details for {account.Name} in '{SettingsFile}' and rerun!");
            }
        }

        private static async Task IterateWorkItems(List<WorkitemRelation> relations, EFU parent, WiqlRelationList wis, Settings account)
        {
            if (relations.Count > 0)
            {
                workitems = await AzDo.GetWorkItems(account, relations.ToList());
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

                    efus[account.Name].Add(efu);
                    parent?.Children.Add(efu.Id);
                    await IterateWorkItems(wis.workItemRelations.Where(x => x.source != null && x.source.id.Equals(wi.id)).ToList(), efu, wis, account);
                }
            }
        }

        private static async Task PromoteChildDescriptionsToParentAsync(Settings account)
        {
            var grouping = efus[account.Name].Where(x => x.Workitemtype.Equals(Task)).GroupBy(x => x.Parent); // .OrderByDescending(x => x.Id)
            ColorConsole.Write($"\nProcess {grouping.Count()} PBIs? (Y/N) ".Yellow());
            var input = Console.ReadLine();
            if (input.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                await PromoteChildDescriptionsToParentAsync(grouping, account).ConfigureAwait(false);
            }
        }

        private static async Task PromoteChildDescriptionsToParentAsync(IEnumerable<IGrouping<int?, EFU>> grouping, Settings account)
        {
            foreach (var group in grouping)
            {
                var parentId = group.Key;
                if (parentId != null)
                {
                    var childDescs = group.Where(x => x.Description != null).Select(x => "<li><b>" + x.Title + "</b><br />" + x.Description + "</li>")?.Distinct()?.ToList();
                    var childStates = group.Where(x => x.Workitemtype == "Task").Select(x => x.State)?.Distinct()?.ToList();
                    var parent = efus[account.Name].SingleOrDefault(x => x.Id.Equals(parentId));

                    // Add if does not exist already
                    if (!parent.Description.Contains(account.DescriptionPrefix) && childDescs != null)
                    {
                        var desc = $"<br /><br /><b><u>{account.DescriptionPrefix}:</u></b><br /><ol type='1'>" + string.Join(string.Empty, childDescs) + "</ol>";
                        var state = parent.CalculateState(childStates);
                        await UpdateParentDescription(parent, desc, state, account).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task<bool> UpdateParentDescription(EFU parent, string desc, string state, Settings account)
        {
            var update = false;
            if (parent != null && parent.Description?.Contains(desc) != true)
            {
                if (!account.ReportOnly)
                {
                    await SaveWorkItemsAsync(parent, desc, state, account).ConfigureAwait(false);
                }
                else
                {
                    ColorConsole.WriteLine($"Updating Description & State ({parent.State} > {state}) for {parent.Id}".Yellow());
                }
            }

            return update;
        }

        private static Task AppendSourceUserStoryDescriptionToTarget()
        {
            throw new NotImplementedException();
        }

        private static Task UpdateTargetUserStoryFieldsFromSource()
        {
            throw new NotImplementedException();
        }

        private static Task MoveSourceTasksToTargetUserStories()
        {
            throw new NotImplementedException();
        }

        private static Task ImportSourcePbisIntoTarget()
        {
            throw new NotImplementedException();
        }

        private static async Task SaveWorkItemsAsync(EFU workItem, string desc, string state, Settings account)
        {
            var ops = new[]
            {
                new Op { op = AddOperation, path = SystemDescription, value = workItem.Description + desc },
                new Op { op = AddOperation, path = SystemState, value = state }
            }.ToList();

            var result = await AzDo.ProcessRequest<dynamic>(account, string.Format(CultureInfo.InvariantCulture, account.WorkItemUpdateUrl, workItem.Id), ops?.ToJson(), true).ConfigureAwait(false);
            ColorConsole.WriteLine($"Updated Description & State ({workItem.State} > {state}) for {workItem.Id}".White().OnDarkGreen());
        }
    }
}
