"""Combine OurAirports and the GeoNames cities500 dataset into a compact local index."""
import csv
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DOWNLOADS = ROOT / 'downloads/map'


def clean(value):
    return ' '.join(value.split())


def main():
    counts = [0, 0]
    with (ROOT / 'windows-bridge/map/places.tsv').open('w', encoding='utf-8', newline='\n') as output:
        with (DOWNLOADS / 'airports-full.csv').open(encoding='utf-8-sig', newline='') as source:
            for item in csv.DictReader(source):
                if item['type'] == 'closed':
                    continue
                row = ['A', item['ident'], item['name'], item['iso_country'],
                       item['latitude_deg'], item['longitude_deg'],
                       ' '.join(item[k] for k in ('icao_code', 'iata_code', 'gps_code', 'local_code', 'municipality'))]
                output.write('\t'.join(map(clean, row)) + '\n')
                counts[0] += 1
        with zipfile.ZipFile(DOWNLOADS / 'cities500.zip') as archive, archive.open('cities500.txt') as source:
            for line in source:
                city = line.decode('utf-8').rstrip('\n').split('\t')
                aliases = [alias for alias in city[3].split(',') if any('\u3400' <= c <= '\u9fff' for c in alias)]
                row = ['C', city[0], city[1], city[8], city[4], city[5], ' '.join([city[2], *aliases[:10]])]
                output.write('\t'.join(map(clean, row)) + '\n')
                counts[1] += 1
    print(f'{counts[0]:,} airports and {counts[1]:,} cities')


if __name__ == '__main__':
    main()
