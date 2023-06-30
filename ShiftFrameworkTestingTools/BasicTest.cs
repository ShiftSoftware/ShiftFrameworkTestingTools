using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using Xunit.Abstractions;

namespace ShiftSoftware.ShiftFrameworkTestingTools
{
    public class BasicTest<DTO, ListDTO>
        where DTO : ShiftEntityDTO
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

        public async Task<DTO> Get(string ID, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.GetAsync($"/api/{ApiItemName}/{ID}");

            var text = await obj.Content.ReadAsStringAsync();

            try
            {
                if (writeResponse)
                    output.WriteLine(JsonNode.Parse(text)!.ToString());
            }
            catch
            {
                output.WriteLine(text);

                return null!;
            }

            var item = JsonNode.Parse(text).Deserialize<ShiftEntityResponse<DTO>>();

            this.AdditionalShiftEntityResponseData = item!.Additional;

            if (ensureSuccessStatusCode)
                obj.EnsureSuccessStatusCode();

            return item!.Entity!;
        }

        public async Task<DTO> PostOrPut(string? ID, DTO dto, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            var httpContent = new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json");

            HttpResponseMessage obj;

            if (ID == null)
                obj = await client.PostAsync($"/api/{ApiItemName}", httpContent);
            else
                obj = await client.PutAsync($"/api/{ApiItemName}/{ID}", httpContent);

            var text = await obj.Content.ReadAsStringAsync();

            try
            {
                if (writeResponse)
                    output.WriteLine(JsonNode.Parse(text)!.ToString());
            }
            catch
            {
                output.WriteLine(text);

                return null!;
            }

            var item = JsonNode.Parse(text).Deserialize<ShiftEntityResponse<DTO>>();

            this.AdditionalShiftEntityResponseData = item!.Additional;

            if (ensureSuccessStatusCode)
                obj.EnsureSuccessStatusCode();

            return item!.Entity!;
        }

        public async Task<DTO> Delete(string ID, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.DeleteAsync($"/api/{ApiItemName}/{ID}");

            var text = await obj.Content.ReadAsStringAsync();

            try
            {
                if (writeResponse)
                    output.WriteLine(JsonNode.Parse(text)!.ToString());
            }
            catch
            {
                output.WriteLine(text);

                return null!;
            }

            var item = JsonNode.Parse(text).Deserialize<ShiftEntityResponse<DTO>>();

            this.AdditionalShiftEntityResponseData = item!.Additional;

            if (ensureSuccessStatusCode)
                obj.EnsureSuccessStatusCode();

            return item!.Entity!;
        }

        public async Task<List<ListDTO>> OdataList(string? queryString = null, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.GetAsync($"/odata/{OdataItemName}{queryString}");

            var text = await obj.Content.ReadAsStringAsync();

            try
            {
                if (writeResponse)
                    output.WriteLine(JsonNode.Parse(text)!.ToString());
            }
            catch
            {
                output.WriteLine(text);

                return null!;
            }

            var items = JsonNode.Parse(text)!["value"].Deserialize<List<ListDTO>>();

            if (ensureSuccessStatusCode)
                obj.EnsureSuccessStatusCode();

            return items!;
        }

        public async Task<List<RevisionDTO>> RevisionList(string ID, bool ensureSuccessStatusCode = true, bool writeResponse = false)
        {
            HttpResponseMessage obj = await client.GetAsync($"/odata/{OdataItemName}/{ID}/revisions");

            var text = await obj.Content.ReadAsStringAsync();

            try
            {
                if (writeResponse)
                    output.WriteLine(JsonNode.Parse(text)!.ToString());
            }
            catch
            {
                output.WriteLine(text);

                return null!;
            }

            var items = JsonNode.Parse(text)!["value"].Deserialize<List<RevisionDTO>>();

            if (ensureSuccessStatusCode)
                obj.EnsureSuccessStatusCode();

            return items!;
        }
    }
}
