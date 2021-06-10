namespace PromoDesc
{
    using System;

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
        [JsonProperty("System.Description")]
        public SystemDescription SystemDescription { get; set; }

        [JsonProperty("System.ChangedDate")]
        public SystemChangedDate SystemChangedDate { get; set; }
    }

    public class SystemDescription
    {
        public string newValue { get; set; }
        public string oldValue { get; set; }
    }

    public class SystemChangedDate
    {
        public DateTime newValue { get; set; }
        public DateTime oldValue { get; set; }
    }
}
