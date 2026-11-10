"""Independent ADS-B feed for the desktop map and the shared screen link."""
import json
import math
from pathlib import Path
import struct
import threading
import time
from urllib.request import Request, urlopen

MAX_TARGETS = 12
API = 'https://opendata.adsb.fi/api/v3'


def read_site(path):
    try:
        settings = json.loads(path.read_text(encoding='utf-8-sig'))
        if settings.get('enabled') is not True:
            return None
        latitude, longitude = float(settings['lat']), float(settings['lon'])
        radius = int(settings['radius'])
        if -90 <= latitude <= 90 and -180 <= longitude <= 180 and 5 <= radius <= 100:
            return latitude, longitude, radius, str(settings.get('name', 'Radar'))[:80]
    except (OSError, TypeError, ValueError, KeyError, AttributeError):
        pass
    return None


def targets_for_site(site, records):
    lat, lon, radius, _ = site
    phi, lambda0 = math.radians(lat), math.radians(lon)
    targets = []
    for record in records:
        try:
            if record.get('alt_baro') == 'ground' or float(record.get('seen_pos', 0)) > 30:
                continue
            latitude, longitude = float(record['lat']), float(record['lon'])
            if not (-90 <= latitude <= 90 and -180 <= longitude <= 180):
                continue
            phi2, delta = math.radians(latitude), math.radians(longitude) - lambda0
            east = math.sin(delta)*math.cos(phi2)
            north = math.cos(phi)*math.sin(phi2)-math.sin(phi)*math.cos(phi2)*math.cos(delta)
            distance = 3440.065*math.atan2(math.hypot(east, north),
                math.sin(phi)*math.sin(phi2)+math.cos(phi)*math.cos(phi2)*math.cos(delta))
            if distance > radius:
                continue
            angle = math.atan2(east, north)
            bearing = float(record.get('track', 0) or 0)
            if not math.isfinite(bearing):
                bearing = 0
            altitude = record.get('alt_baro')
            if not isinstance(altitude, (int, float)) or not math.isfinite(altitude) or abs(altitude) > 150000:
                altitude = None
            callsign = str(record.get('flight') or record.get('r') or record.get('hex') or '').strip()[:7]
            targets.append(dict(lat=latitude, lon=longitude, distance=distance, track=bearing % 360,
                                altitude=altitude, callsign=callsign,
                                east=round(1000*distance/radius*math.sin(angle)),
                                north=round(1000*distance/radius*math.cos(angle))))
        except (AttributeError, ValueError, TypeError, KeyError):
            continue
    return sorted(targets, key=lambda target: target['distance'])[:80]


def pack_radar(status, site=None, targets=()):
    radius, name = (site[2], site[3]) if site else (0, '')
    short_name = name.encode('ascii', 'ignore')[:12] or (b'LIVE RADAR' if site else b'')
    visible = targets[:MAX_TARGETS] if status == 2 else ()
    packet = bytearray(struct.pack('<4B12s', 2, status, radius, len(visible), short_name))
    for target in visible:
        altitude = target['altitude']
        packet.extend(struct.pack('<hhBh7s', target['east'], target['north'],
                                  round(target['track']*256/360) % 256,
                                  -32768 if altitude is None else round(altitude/100),
                                  target['callsign'].encode('ascii', 'replace')[:7]))
    return bytes(packet)


def fetch(site):
    lat, lon, radius, _ = site
    request = Request(f'{API}/lat/{lat:.5f}/lon/{lon:.5f}/dist/{radius}',
                      headers={'User-Agent': 'MyHud desktop radar/1.0'})
    with urlopen(request, timeout=5) as response:
        payload = json.loads(response.read(1_000_000))
    return targets_for_site(site, payload['ac'] or ())


class RadarFeed:
    def __init__(self, path):
        self.path = Path(path)
        self.output = self.path.with_name('radar-live.json')
        self.current = pack_radar(0)
        self.sent = None
        self.sent_at = 0
        self.running = threading.Event()
        self.running.set()
        threading.Thread(target=self._run, name='adsb-feed', daemon=True).start()

    def _publish(self, status, site, targets=()):
        self.current = pack_radar(status, site, targets)
        temporary = self.output.with_name(self.output.name + '.tmp')
        try:
            data = dict(status=status, targets=targets)
            if site:
                data.update(lat=site[0], lon=site[1], radius=site[2], name=site[3])
            temporary.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
            temporary.replace(self.output)
        except OSError:
            pass

    def _run(self):
        site = None
        next_fetch = 0
        next_allowed = 0
        self._publish(0, None)
        while self.running.is_set():
            selected = read_site(self.path)
            if selected != site:
                site = selected
                self._publish(1 if site else 0, site)
                next_fetch = 0
            now = time.monotonic()
            if site and now >= max(next_fetch, next_allowed):
                next_allowed = now + 1.25
                try:
                    status, targets = 2, fetch(site)
                except (OSError, ValueError, TypeError, KeyError):
                    status, targets = 3, ()
                if self.running.is_set() and read_site(self.path) == site:
                    self._publish(status, site, targets)
                next_fetch = time.monotonic() + 5
            time.sleep(.20)

    def packet(self, force=False):
        now = time.monotonic()
        packet = self.current
        if force or self.sent != packet or now - self.sent_at >= 1:
            self.sent, self.sent_at = packet, now
            return packet
        return None

    def close(self):
        self.running.clear()
