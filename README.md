# orcahello-orcasite-proxy
Prototype of integration between OrcaHello and Orcasite without modifying OrcaHello

## Azure service

Currently the proxy is an Azure web app at
[orcahello-orcasite-proxy-bxg3enfgeydmbcca.westus2-01.azurewebsites.net](https://orcahello-orcasite-proxy-bxg3enfgeydmbcca.westus2-01.azurewebsites.net).
Since no interesting content is exposed there yet, the hostname is not important.

### Configurable settings

The following settings are configurable in the
[Azure portal](https://portal.azure.com/#@adminorcasound.onmicrosoft.com/resource/subscriptions/c65c3881-6d6b-4210-94db-5301ef484f17/resourceGroups/AIForOrcas/providers/Microsoft.Web/sites/orcahello-orcasite-proxy/appServices).

Required settings:

* APIKEY: The API key needed to POST detections to orcasite.

Optional settings:

* ORCASITE_HOSTNAME: The hostname of orcasite.  Default: beta.orcasound.net
* POLL_FREQUENCY_IN_MINUTES: The number of minutes at which to poll for new detections.  Default: 5
* ORCAHELLO_MAX_DETECTION_COUNT: The maximum number of OrcaHello detections to GET.  Default: 1000
* ORCAHELLO_TIMEFRAME: The timeframe string for the OrcaHello API. This should be creater than POLL_FREQUENCY_IN_MINUTES.  Default: 1w
