# Data sources and licensing

Every external data source FjordWatch consumes is public and openly licensed. This page is the authoritative attribution and license register; update it whenever a new source is added.

## Live and operational data

### Kystverket AIS

- **What:** Live vessel positions, identification, and dynamic data inside the Norwegian Economic Exclusive Zone, broadcast as NMEA 0183 AIVDM/AIVDO sentences.
- **Access:** TCP socket at `153.44.253.27:5631`. Public, no authentication required.
- **License:** [NLOD 2.0](https://data.norge.no/nlod/en/2.0/) (Norsk lisens for offentlige data).
- **Citation:** Kystverket / Norwegian Coastal Administration. AIS data delivered free of charge under NLOD.
- **Documentation:** <https://kystverket.no/navigasjonstjenester/ais/tilgang-til-ais-data/>
- **Rate limits:** none documented. Service expects long-lived TCP connections; reconnect with backoff on failure.

### Met.no Locationforecast

- **What:** Wind, wave, and weather forecasts at point coordinates.
- **Access:** REST `https://api.met.no/weatherapi/locationforecast/2.0/`. Requires a `User-Agent` header identifying the consumer.
- **License:** [NLOD 2.0](https://data.norge.no/nlod/en/2.0/).
- **Citation:** Norwegian Meteorological Institute (Met.no). Data licensed under NLOD.
- **Documentation:** <https://api.met.no/weatherapi/locationforecast/2.0/documentation>
- **Rate limits:** see Met.no terms; at most a few requests per minute per `User-Agent` for personal use.

### BarentsWatch

- **What:** Public maritime services from the Norwegian government's BarentsWatch portal (vessel info, fishing activity, port calls).
- **Access:** Public APIs; some endpoints require free registration.
- **License:** Mixed; document the license per endpoint when consumed.
- **Documentation:** <https://www.barentswatch.no/en/articles/Open-data/>

### Sjøfartsdirektoratet (Norwegian Maritime Authority)

- **What:** Vessel registry, public regulations, AIS reporting requirements.
- **Access:** Public web search. Scrape only with caching, polite request rate, and `robots.txt` respect.
- **License:** Public records.
- **Documentation:** <https://www.sdir.no/en/>

## Earth observation data

### Copernicus Sentinel-1 SAR

- **What:** C-band Synthetic Aperture Radar imagery (GRD product), 5 m to 40 m resolution depending on mode.
- **Access:** Copernicus Data Space Ecosystem. Free registration required. Use `sentinelsat` or the OData/STAC APIs.
- **License:** [Copernicus Open Access](https://scihub.copernicus.eu/twiki/pub/SciHubWebPortal/TermsConditions/Sentinel_Data_Terms_and_Conditions.pdf).
- **Citation:** Contains modified Copernicus Sentinel data \[YEAR\], processed by FjordWatch.
- **Documentation:** <https://documentation.dataspace.copernicus.eu/>

## Machine learning datasets

### Airbus Ship Detection Challenge

- **What:** Aerial imagery with bounding-box ship annotations.
- **Access:** Kaggle, free.
- **License:** [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/). Non-commercial use only.
- **Use in FjordWatch:** Training the YOLOv8 ship detector for the demo. Because of the non-commercial clause, this dataset is appropriate for an educational portfolio project. If the project ever gains a commercial scope, switch to HRSC2016 or ShipRSImageNet.

### HRSC2016

- **What:** High-resolution ship images and oriented bounding boxes.
- **Access:** Research request via Northwestern Polytechnical University.
- **License:** Research only. Document the license terms before use.

### Synthetic AIS trajectory anomaly dataset

- **What:** Generated from Kystverket AIS replay using documented anomaly injection rules.
- **Access:** Generated in `ml/datasets/synthesize_anomalies.py`.
- **License:** Same as the source AIS data (NLOD).
- **Notes:** Generation is deterministic via a fixed random seed for reproducibility.

## Documents indexed for RAG

The LLM agent's RAG corpus contains scraped public regulations and definitions:

- Norwegian Maritime Authority public regulations.
- Kystverket AIS access policy.
- Definitions of vessel types, AIS message types, common anomalies.

Each ingested document is stored with its source URL, fetch timestamp, and license attribution in the `documents` table. The ingestion pipeline rejects documents that fail the license allowlist.

## License compliance summary

| Source | License | Commercial OK? | Attribution required? |
|---|---|---|---|
| Kystverket AIS | NLOD 2.0 | Yes | Yes |
| Met.no | NLOD 2.0 | Yes | Yes |
| BarentsWatch | Per endpoint | Per endpoint | Per endpoint |
| Sjøfartsdirektoratet | Public records | Yes | Recommended |
| Copernicus Sentinel-1 | Copernicus Open | Yes | Yes |
| Airbus Ship Detection | CC BY-NC 4.0 | No | Yes |
| HRSC2016 | Research only | No | Yes |
| Synthetic anomalies | Inherits NLOD | Yes | Yes |
