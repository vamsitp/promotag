namespace PromoTagz
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
        public DateTime revisedDate { get; set; }
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
    }

    public class SystemTags
    {
        public string newValue { get; set; }
        public string oldValue { get; set; }
    }
}
