using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrcaHelloProxy
{
    public class PeriodicTasks : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PeriodicTasks> _logger;
        private static HttpClient _httpClient = new HttpClient();
        private static string _orcaHelloDetectionsUri = "https://aifororcasdetections.azurewebsites.net/api/detections?Page=1&SortBy=timestamp&SortOrder=desc&Timeframe=24h&Location=all&RecordsPerPage=1000";
        private static string _orcasiteDetectionsUri = "https://live.orcasound.net/api/json/detections?page%5Blimit%5D=100&page%5Boffset%5D=0&fields%5Bdetection%5D=id%2Csource_ip%2Cplaylist_timestamp%2Cplayer_offset%2Clistener_count%2Ctimestamp%2Cdescription%2Cvisible%2Csource%2Ccategory%2Ccandidate_id%2Cfeed_id";
        private static string _orcasitePostDetectionUri = "https://beta.orcasound.net/api/json/detections?fields%5Bdetection%5D=id%2Csource_ip%2Cplaylist_timestamp%2Cplayer_offset%2Clistener_count%2Ctimestamp%2Cdescription%2Cvisible%2Csource%2Ccategory%2Ccandidate_id%2Cfeed_id";
        private static string _orcasiteFeedsUri = "https://beta.orcasound.net/api/json/feeds?fields%5Bfeed%5D=id%2Cname%2Cnode_name%2Cslug%2Clocation_point%2Cintro_html%2Cimage_url%2Cvisible%2Cbucket%2Cbucket_region%2Ccloudfront_url%2Cdataplicity_id%2Corcahello_id";
        private static string? _orcasiteApiKey;

        public PeriodicTasks(IServiceScopeFactory scopeFactory, ILogger<PeriodicTasks> logger)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _orcasiteApiKey = Environment.GetEnvironmentVariable("APIKEY");
        }

        const int _defaultFrequencyToPollInMinutes = 5;
        private static TimeSpan FrequencyToPoll
        {
            get
            {
                string? frequencyToPollInMinutesString = Environment.GetEnvironmentVariable("ORCASOUND_POLL_FREQUENCY_IN_MINUTES");
                int frequencyToPollInMinutes = (int.TryParse(frequencyToPollInMinutesString, out var minutes)) ? minutes : _defaultFrequencyToPollInMinutes;
                return TimeSpan.FromMinutes(frequencyToPollInMinutes);
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Execute business logic.
                    await ExecuteTask();

                    // Schedule the next execution.
                    await Task.Delay(FrequencyToPoll, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing background task.");
                }
            }
        }

        private async Task<JsonElement?> GetDataArrayAsync(string uri)
        {
            try
            {
                string jsonString = await _httpClient.GetStringAsync(uri);
                JsonElement objectElement = JsonSerializer.Deserialize<JsonElement>(jsonString);
                if (objectElement.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogError($"Invalid objectElement kind in ExecuteTask: {objectElement.ValueKind}");
                    return null;
                }
                if (!objectElement.TryGetProperty("data", out var arrayElement))
                {
                    _logger.LogError($"Missing data in ExecuteTask result");
                    return null;
                }
                if (arrayElement.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogError($"Invalid arrayElement kind in ExecuteTask: {arrayElement.ValueKind}");
                    return null;
                }
                return arrayElement;
            } catch (Exception e)
            {
                return null;
            }
        }

        private async Task<JsonElement?> GetRawArrayAsync(string uri)
        {
            try
            {
                string jsonString = await _httpClient.GetStringAsync(uri);
                JsonElement arrayElement = JsonSerializer.Deserialize<JsonElement>(jsonString);
                if (arrayElement.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogError($"Invalid arrayElement kind in ExecuteTask: {arrayElement.ValueKind}");
                    return null;
                }
                return arrayElement;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        private new async Task ExecuteTask()
        {
            _logger.LogInformation("Background task executed.");

            using var scope = _scopeFactory.CreateScope();
            
            try
            {
                JsonElement? orcasiteFeedsArray = await GetDataArrayAsync(_orcasiteFeedsUri);
                if (orcasiteFeedsArray == null)
                {
                    _logger.LogError("Failed to retrieve orcasite feeds.");
                    return;
                }

                // Query detections from OrcaHello over the past 24 hours.
                // We really only need to get all that haven't been submitted already,
                // but for now we will just get up to 1000 detections from the last 24 hours.
                JsonElement? orcaHelloDetectionsArray = await GetRawArrayAsync(_orcaHelloDetectionsUri);
                if (orcaHelloDetectionsArray == null)
                {
                    _logger.LogError("Failed to retrieve orcahello detections.");
                    return;
                }

                // Query the last 100 detections already on Orcasite.
                JsonElement? orcasiteDetectionsArray = await GetDataArrayAsync(_orcasiteDetectionsUri);
                if (orcasiteDetectionsArray == null)
                {
                    _logger.LogError("Failed to retrieve orcasite detections.");
                    return;
                }

                // Process each detection in the OrcaHello detections (starting from the oldest).
                foreach (JsonElement orcaHelloDetection in orcaHelloDetectionsArray.Value.EnumerateArray().Reverse())
                {
                    if (!orcaHelloDetection.TryGetProperty("id", out var id))
                    {
                        _logger.LogError($"Missing id in ExecuteTask result");
                        continue;
                    }
                    if (id.ValueKind != JsonValueKind.String)
                    {
                        _logger.LogError($"Invalid id kind in ExecuteTask: {id.ValueKind}");
                        continue;
                    }

                    if (!orcaHelloDetection.TryGetProperty("location", out var location))
                    {
                        _logger.LogError($"Missing location in ExecuteTask result");
                        continue;
                    }
                    if (location.ValueKind != JsonValueKind.Object)
                    {
                        _logger.LogError($"Invalid location kind in ExecuteTask: {location.ValueKind}");
                        continue;
                    }

                    if (!location.TryGetProperty("name", out var locationName))
                    {
                        _logger.LogError($"Missing location.name in ExecuteTask result");
                        continue;
                    }
                    if (locationName.ValueKind != JsonValueKind.String)
                    {
                        _logger.LogError($"Invalid location.name kind in ExecuteTask: {locationName.ValueKind}");
                        continue;
                    }
                    string locationNameString = locationName.GetString() ?? "";

                    // Get feed ID from location name.
                    string? feedId = GetFeedId(locationNameString, orcasiteFeedsArray.Value);
                    if (feedId == null)
                    {
                        _logger.LogError($"Couldn't find feed id for: {locationNameString}");
                        continue;
                    }

                    // Get timestamp according to OrcaHello.
                    if (!orcaHelloDetection.TryGetProperty("timestamp", out var timestamp))
                    {
                        _logger.LogError($"Missing timestamp in ExecuteTask result");
                        continue;
                    }
                    if (timestamp.ValueKind != JsonValueKind.String)
                    {
                        _logger.LogError($"Invalid timestamp kind in ExecuteTask: {timestamp.ValueKind}");
                        continue;
                    }

                    // Get comments from OrcaHello.
                    if (!orcaHelloDetection.TryGetProperty("comments", out var comments))
                    {
                        _logger.LogError($"Missing comments in ExecuteTask result");
                        continue;
                    }
                    string? commentsString = comments.ValueKind == JsonValueKind.String ? comments.GetString() : null;

                    // Compose a detections post.
                    JsonObject newDetection = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["type"] = "detection",
                            //["id"] = JsonValue.Create(id),
                            ["attributes"] = new JsonObject
                            {
                                ["description"] = JsonValue.Create(comments),
                                ["feed_id"] = JsonValue.Create(feedId),
                                ["timestamp"] = JsonValue.Create(timestamp),

                                // "source": "machine" is implied by a POST.
                                //
                                // "category" is currently disallowed by the API, even though
                                // it would make sense to add "category": "whale" to distinguish
                                // it from, say, a shipnoise detector.
                                //
                                // "playlist_timestamp" and "player_offset" are computed
                                // from the timestamp by the API service so are not needed here.
                            }
                        }
                    };

                    // Try posting it.
                    string newDetectionString = newDetection.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _orcasitePostDetectionUri)
                    {
                        Content = new StringContent(newDetectionString)
                    };
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _orcasiteApiKey);

                    HttpResponseMessage response = await _httpClient.SendAsync(request);

                    // Optionally handle response
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Detection posted successfully!");
                    }
                    else
                    {
                        string message = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Error: {response.StatusCode} - {message}");
                    }
                }
            }
            catch (Exception e)
            {

            }
#if false
            OrcanodeMonitorContext context = scope.ServiceProvider.GetRequiredService<OrcanodeMonitorContext>();

            await Fetcher.UpdateDataplicityDataAsync(context, _logger);

            await Fetcher.UpdateOrcasoundDataAsync(context, _logger);

            await MezmoFetcher.UpdateMezmoDataAsync(context, _logger);

            await Fetcher.UpdateS3DataAsync(context, _logger);

            await Fetcher.UpdateOrcaHelloDataAsync(context, _logger);
#endif
        }

        private string? GetFeedId(string nameToFind, JsonElement feedsArray)
        {
            foreach (JsonElement feed in feedsArray.EnumerateArray())
            {
                if (!feed.TryGetProperty("attributes", out var attributes))
                {
                    _logger.LogError($"Missing attributes in ExecuteTask result");
                    continue;
                }
                if (attributes.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogError($"Invalid attributes kind in ExecuteTask: {attributes.ValueKind}");
                    continue;
                }
                if (!attributes.TryGetProperty("name", out var name))
                {
                    _logger.LogError($"Missing name in ExecuteTask result");
                    continue;
                }
                if (name.ValueKind != JsonValueKind.String)
                {
                    _logger.LogError($"Invalid name kind in ExecuteTask: {name.ValueKind}");
                    continue;
                }
                if (name.GetString() != nameToFind)
                {
                    continue;
                }

                // Found the name, get the id.
                if (!feed.TryGetProperty("id", out var id))
                {
                    _logger.LogError($"Missing id in ExecuteTask result");
                    continue;
                }
                if (id.ValueKind != JsonValueKind.String)
                {
                    _logger.LogError($"Invalid id kind in ExecuteTask: {id.ValueKind}");
                    continue;
                }
                return id.GetString();
            }
            return null;
        }
    }
}
