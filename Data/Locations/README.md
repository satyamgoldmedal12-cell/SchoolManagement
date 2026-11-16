# Offline place names

Data source: [GeoNames](https://www.geonames.org/), downloaded 2026-11-03.
License: [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/).

`places.tsv.gz` contains 238,530 points from the worldwide
[cities500 extract](https://download.geonames.org/export/dump/cities500.zip), supplemented by populated
place sections (`PPLX`) from the [India extract](https://download.geonames.org/export/dump/IN.zip).
Region and country labels come from GeoNames `admin1CodesASCII.txt` and `countryInfo.txt`.
Historical, abandoned and destroyed populated-place codes are excluded. This derived extract retains
GeoNames ID, place name, latitude, longitude, region and country, in that column order.

The lookup reports the nearest available point within 25 km. It does not determine street addresses,
postal addresses, neighbourhood boundaries or the user's actual administrative membership. A nearest
point across a border can be returned; the history UI therefore labels the result **Near** and shows
the distance. Rural and sparse regions can have no match. Data completeness and accuracy vary.

This file is packaged with the app, cached once in memory and indexed by latitude degree. Browser
coordinates remain within the school application; the lookup makes no external API request and needs
no API key. It runs only after the existing metadata validation accepts browser-shared coordinates.
Old history is not backfilled or relabelled automatically. Login continues if the file is unavailable.
