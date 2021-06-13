namespace PromoDesc
{
    using System;
    using System.Text;

    public class Settings
    {
        private const string ApiVersion = "6.0";

        public string Org { get; set; }
        public string Project { get; set; }
        public string Token { get; set; }
        public string DescriptionPrefix { get; set; }
        public string WorkItemsQuery { get; set; }
        public bool ReportOnly { get; set; }

        public string Pat => Convert.ToBase64String(Encoding.ASCII.GetBytes($":{Token ?? string.Empty}"));

        public string BaseUrl => $"https://dev.azure.com/{Org}/{Project}/_apis/wit";

        public string RelationsQueryPath => $"{BaseUrl}/wiql?api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        public string WorkItemsQueryPath => $"{BaseUrl}/workitems?ids={{0}}&api-version={ApiVersion}"; //"queries/Shared Queries/EFUs";

        public string WorkItemUpdateUrl => $"{BaseUrl}/workItems/{{0}}?api-version={ApiVersion}";
    }
}