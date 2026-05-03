# Disclaimer

FjordWatch is an independent open-source educational and portfolio project authored by Erdem Kilci.

It is **not** affiliated with, endorsed by, or representing any company, agency, or organization, including but not limited to:

- TOMRA ASA (the author's current employer)
- Kongsberg Gruppen
- DNV
- Cognite
- Maritime Robotics
- Equinor
- Bouvet, Sopra Steria, Computas
- The Norwegian Coastal Administration (Kystverket)
- The Norwegian Maritime Authority (Sjøfartsdirektoratet)
- The Norwegian government or armed forces
- The European Space Agency (ESA) or the Copernicus programme
- The Norwegian Meteorological Institute (Met.no)

## Data licensing

Public data is consumed under the data provider's published licenses:

- **Kystverket AIS data** is used under NLOD (Norsk lisens for offentlige data).
- **Sentinel-1 SAR imagery** is used under the Copernicus open license.
- **Met.no weather data** is used under NLOD.
- **BarentsWatch data** is used under the licenses published per endpoint.

License attributions and access details are tracked in `docs/data-sources.md`.

## Not for operational use

The system is **not** intended for, and **must not** be used for:

- Operational maritime surveillance
- Law enforcement targeting or evidence
- Military or paramilitary targeting or planning
- Search and rescue dispatch
- Any decision affecting the safety of life, property, or vessels
- Any commercial decision-making

The detection logic is illustrative. Vessel detections, anomaly scores, and "dark vessel" flags can be wrong for many reasons including but not limited to: AIS reporting gaps below the 300 GT threshold, weather artefacts in SAR, model false positives on rocks and offshore platforms, and stale or out-of-window correlation.

## No warranty

The software is provided "as is" without warranty of any kind, express or implied. The author accepts no liability for any use, misuse, or consequence arising from the software or its outputs.
