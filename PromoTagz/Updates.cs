namespace PromoTagz
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;

    public class Updates
    {
        public int count { get; set; }
        public Value[] value { get; set; }
    }

    public class Value
    {
        public int id { get; set; }
        public int workItemId { get; set; }
        public int rev { get; set; }
        public Revisedby revisedBy { get; set; }
        public UpdatedFields fields { get; set; }
        public string url { get; set; }
    }

    public class Revisedby
    {
        public string id { get; set; }
        public string name { get; set; }
        public string displayName { get; set; }
        public string url { get; set; }
    }

    public class UpdatedFields
    {
        [JsonProperty("System.Tags")]
        public SystemTags SystemTags { get; set; }

        [JsonProperty("System.ChangedDate")]
        public SystemChangedDate SystemChangedDate { get; set; }
    }

    public class SystemTags
    {
        public string newValue { get; set; }
        public string oldValue { get; set; }

        [JsonIgnore]
        public IEnumerable<string> newValues => this.newValue?.Split(Program.TagsDelimiters, StringSplitOptions.RemoveEmptyEntries).Select(x => x?.Trim());
        [JsonIgnore]
        public IEnumerable<string> oldValues => this.oldValue?.Split(Program.TagsDelimiters, StringSplitOptions.RemoveEmptyEntries).Select(x => x?.Trim());
    }

    public class SystemChangedDate
    {
        public DateTime newValue { get; set; }
        public DateTime oldValue { get; set; }
    }
}
