using System;
using System.Collections.Generic;
using System.Text;

namespace PromoTagz
{
    public class WorkItemTag
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string CurrentTags { get; set; }
        public string Tag { get; set; }
        public DateTime? Added { get; set; }
        public DateTime? Removed { get; set; }
        public string Duration => Added.HasValue && Removed.HasValue ? Removed.Value.Subtract(Added.Value).ToString("d\'d'\\ h\'h'\\ mm\'m'") : string.Empty;
        public string ChangedBy { get; set; }
    }
}
