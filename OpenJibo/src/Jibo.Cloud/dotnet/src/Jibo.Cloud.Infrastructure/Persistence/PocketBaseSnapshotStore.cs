using System.Text;
using System.Text.Json;

namespace Jibo.Cloud.Infrastructure.Persistence;

internal sealed class PocketBaseSnapshotStore(string connectionString, string snapshotName) : ISnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string GetCollectionName() => $"{snapshotName}_snapshots";

    private string GetBaseUrl() => connectionString.TrimEnd('/');

    public TSnapshot? Load<TSnapshot>() where TSnapshot : class
    {
        try
        {
            using var httpClient = new HttpClient();
            var collectionName = GetCollectionName();
            var url = $"{GetBaseUrl()}/api/collections/{collectionName}/records?filter=name='{snapshotName}'&limit=1";

            var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;

            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var result = JsonSerializer.Deserialize<PocketBaseListResponse<TSnapshot>>(content, JsonOptions);

            if (result?.Items != null && result.Items.Count > 0)
            {
                return result.Items[0];
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public void Save<TSnapshot>(TSnapshot snapshot) where TSnapshot : class
    {
        try
        {
            using var httpClient = new HttpClient();
            var collectionName = GetCollectionName();
            var baseUrl = GetBaseUrl();

            // First, check if a record with this name already exists
            var listUrl = $"{baseUrl}/api/collections/{collectionName}/records?filter=name='{snapshotName}'&limit=1";
            var listResponse = httpClient.GetAsync(listUrl).GetAwaiter().GetResult();

            if (listResponse.IsSuccessStatusCode)
            {
                var listContent = listResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var existingResult = JsonSerializer.Deserialize<PocketBaseListResponse<PocketBaseRecord<TSnapshot>>>(listContent, JsonOptions);

                if (existingResult?.Items != null && existingResult.Items.Count > 0)
                {
                    // Update existing record
                    var existingRecord = existingResult.Items[0];
                    var updateUrl = $"{baseUrl}/api/collections/{collectionName}/records/{existingRecord.Id}";
                    var updateData = new PocketBaseRecord<TSnapshot>
                    {
                        Name = snapshotName,
                        Data = snapshot
                    };
                    var json = JsonSerializer.Serialize(updateData, JsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    httpClient.PatchAsync(updateUrl, content).GetAwaiter().GetResult();
                    return;
                }
            }

            // Create new record
            var createUrl = $"{baseUrl}/api/collections/{collectionName}/records";
            var createData = new PocketBaseRecord<TSnapshot>
            {
                Name = snapshotName,
                Data = snapshot
            };
            var createJson = JsonSerializer.Serialize(createData, JsonOptions);
            var createContent = new StringContent(createJson, Encoding.UTF8, "application/json");
            httpClient.PostAsync(createUrl, createContent).GetAwaiter().GetResult();
        }
        catch
        {
            // Silently fail - in production, we'd want to log this
        }
    }

    private class PocketBaseListResponse<T>
    {
        public List<T>? Items { get; set; }
        public int TotalItems { get; set; }
    }

    private class PocketBaseRecord<T>
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public T? Data { get; set; }
    }
}
