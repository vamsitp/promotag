namespace PromoDesc
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;

    using Flurl.Http;
    using Flurl.Http.Content;

    using Newtonsoft.Json;

    internal class AzDo
    {
        private const string JsonPatchMediaType = "application/json-patch+json";
        private const string JsonMediaType = "application/json";
        private const string AuthHeader = "Authorization";
        private const string BasicAuth = "Basic ";

        internal static async Task<List<WorkItem>> GetWorkItems(Settings account, List<WorkitemRelation> items)
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
                        var workItems = await ProcessRequest<WorkItems>(account, string.Format(account.WorkItemsQueryPath, ids));
                        if (workItems != null)
                        {
                            result.AddRange(workItems.Items);
                        }
                    }
                }
            }

            return result;
        }

        internal static async Task<T> ProcessRequest<T>(Settings account, string path, string content = null, bool patch = false)
        {
            try
            {
                // https://www.visualstudio.com/en-us/docs/integrate/api/wit/samples
                Trace.TraceInformation($"BaseAddress: {account.Org} | Path: {path} | Content: {content}");
                IFlurlResponse queryHttpResponseMessage;
                var request = path.WithHeader(AuthHeader, BasicAuth + account.Pat);

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
                err.WriteError();
                return default(T);
            }
        }
    }

    internal class Op
    {
        public string op { get; set; }

        public string path { get; set; }

        public dynamic value { get; set; }
    }
}
