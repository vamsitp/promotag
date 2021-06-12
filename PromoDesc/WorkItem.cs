namespace PromoDesc
{
    using System;
    using System.Collections.Generic;

    using Newtonsoft.Json;

    public class WiqlList
    {
        public string queryType { get; set; }
        public string queryResultType { get; set; }
        public DateTime asOf { get; set; }
        public Column[] columns { get; set; }
        public Sortcolumn[] sortColumns { get; set; }
        public WiqlWorkitem[] workItems { get; set; }
    }

    public class WiqlRelationList
    {
        public string queryType { get; set; }
        public string queryResultType { get; set; }
        public DateTime asOf { get; set; }
        public Column[] columns { get; set; }
        public Sortcolumn[] sortColumns { get; set; }
        public WorkitemRelation[] workItemRelations { get; set; }
    }

    public class Column
    {
        public string referenceName { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class Sortcolumn
    {
        public Field field { get; set; }
        public bool descending { get; set; }
    }

    public class Field
    {
        public string referenceName { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public class WiqlWorkitem
    {
        public int id { get; set; }
        public string url { get; set; }
    }

    public class WorkItem
    {
        public int id { get; set; }
        public int rev { get; set; }
        public Fields fields { get; set; }
        public Links _links { get; set; }
        public string url { get; set; }
    }

    public class Fields
    {
        [JsonProperty("System.AreaPath")]
        public string SystemAreaPath { get; set; }

        [JsonProperty("System.TeamProject")]
        public string SystemTeamProject { get; set; }

        [JsonProperty("System.IterationPath")]
        public string SystemIterationPath { get; set; }

        [JsonProperty("System.WorkItemType")]
        public string SystemWorkItemType { get; set; }

        [JsonProperty("System.State")]
        public string SystemState { get; set; }

        [JsonProperty("System.Title")]
        public string SystemTitle { get; set; }

        [JsonProperty("System.Description")]
        public string SystemDescription { get; set; }

        [JsonProperty("System.Tags")]
        public string SystemTags { get; set; }
    }

    public class Links
    {
        public Self self { get; set; }
        public Workitemupdates workItemUpdates { get; set; }
        public Workitemrevisions workItemRevisions { get; set; }
        public Workitemhistory workItemHistory { get; set; }
        public Html html { get; set; }
        public Workitemtype workItemType { get; set; }
        public LinkFields fields { get; set; }
    }

    public class Self
    {
        public string href { get; set; }
    }

    public class Workitemupdates
    {
        public string href { get; set; }
    }

    public class Workitemrevisions
    {
        public string href { get; set; }
    }

    public class Workitemhistory
    {
        public string href { get; set; }
    }

    public class Html
    {
        public string href { get; set; }
    }

    public class Workitemtype
    {
        public string href { get; set; }
    }

    public class LinkFields
    {
        public string href { get; set; }
    }

    public class WorkitemRelation
    {
        public WiqlWorkitem target { get; set; }
        public string rel { get; set; }
        public WiqlWorkitem source { get; set; }
    }

    public class EFU
    {
        public EFU()
        {
            this.Children = new List<int>();
        }

        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string State { get; set; }
        public string Workitemtype { get; set; }
        public List<int> Children { get; set; }
        public int? Parent { get; set; }
        public IList<string> Tags { get; set; }
    }

    public class WorkItems
    {
        [JsonProperty("count")]
        public int Count { get; set; }
        [JsonProperty("value")]
        public WorkItem[] Items { get; set; }
    }
}
