using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using Xunit.Abstractions;

namespace ShiftSoftware.ShiftFrameworkTestingTools
{
    public class BasicTest<DTO, ListDTO>
        where DTO : ShiftEntityViewAndUpsertDTO
        where ListDTO : ShiftEntityListDTO
    {
        public HttpClient client;
        public ITestOutputHelper output;

        public string ApiItemName { get; set; }
        public string OdataItemName { get; set; }

        public Dictionary<string, object>? AdditionalShiftEntityResponseData;

        public BasicTest(string apiItemName, string odataItemItem, HttpClient client, ITestOutputHelper output)
        {
            this.ApiItemName = apiItemName;
            this.OdataItemName = odataItemItem;

            this.client = client;
            this.output = output;
        }

        public async Task<DTO?> Get(string ID, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.GetAsync($"/api/{ApiItemName}/{ID}");

            return await ParseResponse<DTO>(obj, ResponseTypes.ShiftEntity, writeResponse, ensureSuccessStatusCode);
        }

        public async Task<DTO?> PostOrPut(string? ID, DTO dto, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            var httpContent = new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json");

            HttpResponseMessage obj;

            if (ID == null)
                obj = await client.PostAsync($"/api/{ApiItemName}", httpContent);
            else
                obj = await client.PutAsync($"/api/{ApiItemName}/{ID}", httpContent);

            return await ParseResponse<DTO>(obj, ResponseTypes.ShiftEntity, writeResponse, ensureSuccessStatusCode);
        }

        public async Task<DTO?> Delete(string ID, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.DeleteAsync($"/api/{ApiItemName}/{ID}");

            return await ParseResponse<DTO>(obj, ResponseTypes.ShiftEntity, writeResponse, ensureSuccessStatusCode);
        }

        public async Task<List<ListDTO>?> OdataList(string? queryString = null, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.GetAsync($"/api/{OdataItemName}{queryString}");

            return await ParseResponse<List<ListDTO>>(obj, ResponseTypes.OData, writeResponse, ensureSuccessStatusCode);
        }

        public async Task<List<RevisionDTO>?> RevisionList(string ID, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.GetAsync($"/api/{OdataItemName}/{ID}/revisions");

            return await ParseResponse<List<RevisionDTO>>(obj, ResponseTypes.OData, writeResponse, ensureSuccessStatusCode);
        }

        private enum ResponseTypes
        {
            ShiftEntity = 1,
            OData = 2
        }
        private async Task<T?> ParseResponse<T>(HttpResponseMessage httpResponseMessage, ResponseTypes responseType, bool writeResponse, bool ensureSuccessStatusCode)
        {
            var text = await httpResponseMessage.Content.ReadAsStringAsync();

            JsonNode? jsonNode = null;

            try
            {
                jsonNode = JsonNode.Parse(text);
            }
            catch
            {

            }

            if (writeResponse)
                output.WriteLine(jsonNode?.ToString() ?? text);

            T? response = default(T);

            if (jsonNode != null)
            {
                if (responseType == ResponseTypes.ShiftEntity)
                {
                    var shiftEntityResponse = jsonNode.Deserialize<ShiftEntityResponse<T>>();

                    this.AdditionalShiftEntityResponseData = shiftEntityResponse!.Additional;

                    response = shiftEntityResponse!.Entity;
                }
                else if (responseType == ResponseTypes.OData)
                {
                    response = jsonNode["value"].Deserialize<T>();
                }
            }

            if (ensureSuccessStatusCode)
                httpResponseMessage.EnsureSuccessStatusCode();

            return response;
        }
    }
}
