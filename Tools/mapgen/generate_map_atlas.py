#!/usr/bin/env python3
"""
Bake the terminal map atlas from Natural Earth (public domain).

    python3 Tools/mapgen/generate_map_atlas.py <natural-earth-geojson-dir>

Writes Brink/Assets/Scripts/UI/MapAtlas.Generated.cs. The game never reads
geographic data at runtime: every outline, state line, terrain cell and site
position is baked here into run-length-encoded character rasters, and the
renderers in AsciiCountryMap / AsciiWorldMap resample them into whatever grid
the panel measures. Requires shapely. The geojson files are the ones published
at github.com/nvkelso/natural-earth-vector/tree/master/geojson:

    ne_50m_admin_0_countries, ne_10m_admin_1_states_provinces,
    ne_50m_geography_regions_polys, ne_50m_rivers_lake_centerlines,
    ne_50m_lakes

This is presentation data only. Nothing in the simulation reads it; distance
and reach still come from WorldFactory's authored mapX/mapY (GDD §16).
"""
import json, math, os, sys
from collections import Counter, defaultdict
from shapely.geometry import shape, Point, box
from shapely.affinity import translate, scale as shp_scale
from shapely.ops import unary_union
from shapely.strtree import STRtree

SRC_W, SRC_H = 208, 56          # country rasters are fitted inside this box
WORLD_W, WORLD_H = 312, 62      # world raster, lon -180..180, lat 84..-58
WORLD_LAT_TOP, WORLD_LAT_BOTTOM = 84.0, -58.0
CELL_ASPECT = 2.0               # a terminal cell is about twice as tall as wide
SUB = 3                         # sub-samples per cell edge

# Region alphabet: no digits, because the run-length encoding writes counts
# in decimal ahead of each cell character.
ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&*+/:;<=>?@^_{|}~"

# Main frame per country (lon_min, lon_max, lat_min, lat_max). Anything outside
# is overseas and dropped, except the USA insets handled below.
FRAMES = {
    "USA": (-125.0, -66.5, 20.0, 49.6),
    "CHN": (73.0, 135.5, 17.5, 53.8),
    "RUS": (26.0, 191.0, 41.0, 78.0),
    "IND": (68.0, 97.5, 6.5, 37.2),
    "DEU": (5.8, 15.1, 47.2, 55.1),
    "JPN": (128.5, 146.0, 30.8, 45.6),
    "BRA": (-74.2, -34.7, -33.9, 5.4),
    "TUR": (25.6, 44.9, 35.8, 42.2),
    "NGA": (2.6, 14.7, 4.2, 13.95),
    "SAU": (34.4, 55.8, 16.3, 32.2),
    "AUS": (112.8, 154.0, -43.8, -10.5),
    "KOR": (125.9, 129.7, 33.1, 38.7),
    "MEX": (-117.2, -86.6, 14.5, 32.8),
    "IDN": (95.0, 141.1, -11.1, 6.0),
    "POL": (14.1, 24.2, 49.0, 54.9),
    "KAZ": (46.4, 87.4, 40.5, 55.5),
    "GBR": (-8.7, 1.8, 49.9, 60.9),
    "FRA": (-4.9, 9.6, 41.3, 51.1),
    "ITA": (6.6, 18.6, 36.6, 47.1),
    "CAN": (-141.1, -52.6, 41.6, 72.5),
    "EGY": (24.7, 36.9, 22.0, 31.7),
    "ZAF": (16.4, 32.9, -34.9, -22.1),
    "ARG": (-73.6, -53.6, -55.1, -21.8),
    "VNM": (102.1, 109.5, 8.5, 23.4),
}
ORDER = list(FRAMES.keys())

# Internal lines: admin-1 units when there are few enough to read, otherwise
# a coarser grouping Natural Earth already carries, otherwise none.
GROUPING = {"GBR": "geonunit", "FRA": "region", "ITA": "region", "JPN": "region",
            "RUS": "region", "VNM": "region", "TUR": None}

# Where each authored site physically is (lon, lat). Keyed by location id.
# CONTESTED_LANE is deliberately generic and has no real position.
SITES = {
    "USA_CAP": (-77.04, 38.90), "USA_PRT": (-76.30, 36.95), "USA_ENR": (-94.50, 29.80),
    "CHN_CAP": (116.40, 39.90), "CHN_PRT": (121.50, 31.20), "CHN_IND": (113.30, 23.10), "CHN_AIR": (110.30, 20.00),
    "RUS_CAP": (37.60, 55.75), "RUS_ENR": (73.40, 61.00), "RUS_AIR": (30.30, 59.90), "RUS_PRT": (33.40, 69.10),
    "IND_CAP": (77.20, 28.60), "IND_IND": (73.30, 19.60), "IND_AIR": (75.00, 31.00), "IND_PRT": (72.85, 18.95),
    "DEU_CAP": (13.40, 52.50), "DEU_IND": (7.20, 51.45), "DEU_AIR": (7.60, 49.44), "DEU_PRT": (9.97, 53.55), "DEU_CHK": (11.20, 54.50),
    "JPN_CAP": (139.70, 35.70), "JPN_PRT": (139.64, 35.44), "JPN_CHK": (140.60, 41.50),
    "BRA_CAP": (-47.90, -15.80), "BRA_ENR": (-45.50, -24.50), "BRA_PRT": (-46.30, -23.95), "BRA_MAT": (-50.20, -6.10),
    "TUR_CAP": (32.85, 39.93), "TUR_CHK": (29.05, 41.10), "TUR_AIR": (35.43, 37.00), "TUR_PRT": (36.20, 36.60),
    "NGA_CAP": (7.50, 9.06), "NGA_ENR": (6.20, 4.90), "NGA_PRT": (3.40, 6.45), "NGA_MAT": (8.90, 9.90),
    "SAU_CAP": (46.70, 24.70), "SAU_ENR": (49.60, 26.30), "SAU_PRT": (38.06, 24.09), "SAU_CHK": (41.80, 16.90),
    "AUS_CAP": (149.13, -35.30), "AUS_MAT": (118.60, -22.30), "AUS_AIR": (132.40, -14.50), "AUS_PRT": (115.75, -32.05),
    "KOR_CAP": (126.98, 37.57), "KOR_IND": (129.30, 35.54), "KOR_PRT": (129.04, 35.10),
    "MEX_CAP": (-99.13, 19.43), "MEX_IND": (-101.30, 20.90), "MEX_PRT": (-104.30, 19.05), "MEX_CHK": (-95.00, 17.00),
    "IDN_CAP": (106.85, -6.20), "IDN_CHK": (101.00, 2.50), "IDN_PRT": (106.88, -6.10), "IDN_CHK2": (115.70, -8.70),
    "POL_CAP": (21.00, 52.23), "POL_PAS": (22.90, 54.10), "POL_PRT": (18.65, 54.40),
    "KAZ_CAP": (71.45, 51.17), "KAZ_MAT": (51.90, 47.10),
    "GBR_CAP": (-0.13, 51.50), "GBR_PRT": (-4.80, 56.07),
    "FRA_CAP": (2.35, 48.86), "FRA_PRT": (5.93, 43.12),
    "ITA_CAP": (12.50, 41.90), "ITA_IND": (10.00, 45.30), "ITA_PRT": (8.93, 44.40),
    "CAN_CAP": (-75.70, 45.42), "CAN_ENR": (-113.50, 53.50), "CAN_MAT": (-80.00, 49.00), "CAN_PRT": (-63.57, 44.65),
    "EGY_CAP": (31.24, 30.04), "EGY_CHK": (32.55, 30.00), "EGY_PRT": (29.90, 31.20),
    "ZAF_CAP": (28.19, -25.75), "ZAF_MAT": (29.20, -26.40), "ZAF_PRT": (31.03, -29.87),
    "ARG_CAP": (-58.38, -34.60), "ARG_PRT": (-59.00, -34.10),
    "VNM_CAP": (105.85, 21.03), "VNM_IND": (106.40, 20.70), "VNM_PRT": (107.00, 10.50),
}


# Natural Earth classes a few agricultural regions as deserts, and repeats a
# river under a second language's name; neither belongs in a terrain readout.
SKIP_NAMES = {"Punjab", "Bénoué", "Rhin", "Damietta Branch", "Rosetta Branch"}


def load(d, name):
    with open(os.path.join(d, name + ".geojson"), encoding="utf-8") as f:
        return json.load(f)["features"]


def unwrap(geom, pivot):
    """Shift longitudes across the antimeridian so a country is contiguous."""
    if pivot is None: return geom
    parts = list(geom.geoms) if hasattr(geom, "geoms") else [geom]
    out = []
    for p in parts:
        if p.is_empty: continue
        c = p.centroid.x
        if pivot > 0 and c < 0: p = translate(p, 360, 0)
        if pivot < 0 and c > 0: p = translate(p, -360, 0)
        out.append(p)
    return unary_union(out)


def clip_frame(geom, frame):
    return geom.intersection(box(frame[0], frame[2], frame[1], frame[3]))


def usa_insets(geom):
    """Alaska and Hawaii drawn in the bottom-left, the way US maps do."""
    geom = unwrap(geom, -1)
    alaska = geom.intersection(box(-190, 50, -129, 72))
    hawaii = geom.intersection(box(-161, 18, -154, 23))
    ak = translate(shp_scale(alaska, 0.30, 0.30, origin=(-152, 62)), 36.0, -38.5)
    hi = translate(hawaii, 52.5, 1.0)
    return ak, hi


def rle(row):
    out, i = [], 0
    while i < len(row):
        j = i
        while j < len(row) and row[j] == row[i]: j += 1
        n = j - i
        out.append((str(n) if n > 1 else "") + row[i])
        i = j
    return "".join(out)


def cs(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


class Raster:
    def __init__(self, x0, y0, cw, ch, w, h, xscale):
        self.x0, self.y0, self.cw, self.ch, self.w, self.h, self.xscale = x0, y0, cw, ch, w, h, xscale

    def lonlat(self, cx, cy, sx, sy):
        x = self.x0 + (cx + (sx + 0.5) / SUB) * self.cw
        y = self.y0 - (cy + (sy + 0.5) / SUB) * self.ch
        return x / self.xscale, y

    def sample(self, geoms, values, default=" ", threshold=3, priority=None):
        tree = STRtree(geoms)
        grid = []
        for cy in range(self.h):
            row = []
            for cx in range(self.w):
                votes = Counter()
                for sy in range(SUB):
                    for sx in range(SUB):
                        pt = Point(*self.lonlat(cx, cy, sx, sy))
                        for idx in tree.query(pt):
                            if geoms[idx].contains(pt):
                                votes[values[idx]] += 1
                                break
                total = sum(votes.values())
                if total >= threshold:
                    if priority:
                        row.append(min(votes, key=lambda v: (priority.index(v), -votes[v])))
                    else:
                        row.append(votes.most_common(1)[0][0])
                else:
                    row.append(default)
            grid.append("".join(row))
        return grid


def country_raster(frame):
    lon0, lon1, lat0, lat1 = frame
    xscale = math.cos(math.radians((lat0 + lat1) / 2))
    xr, yr = (lon1 - lon0) * xscale, lat1 - lat0
    s = max(xr / SRC_W, yr / (CELL_ASPECT * SRC_H))
    w, h = max(1, round(xr / s)), max(1, round(yr / (CELL_ASPECT * s)))
    return Raster(lon0 * xscale, lat1, s, s * CELL_ASPECT, w, h, xscale)


def main(d, out):
    admin0 = {f["properties"]["ADM0_A3"]: shape(f["geometry"]) for f in load(d, "ne_50m_admin_0_countries")}
    admin1 = defaultdict(list)
    for f in load(d, "ne_10m_admin_1_states_provinces"):
        p = f["properties"]
        if p["adm0_a3"] in FRAMES: admin1[p["adm0_a3"]].append((p, shape(f["geometry"]).buffer(0)))
    terrain = []
    for f in load(d, "ne_50m_geography_regions_polys"):
        cls = f["properties"]["FEATURECLA"]
        kind = "m" if cls in ("Range/mtn",) else "d" if cls == "Desert" else None
        if kind: terrain.append((kind, f["properties"]["NAME"].title(), shape(f["geometry"]).buffer(0)))
    rivers = [(f["properties"]["name"] or "", shape(f["geometry"]))
              for f in load(d, "ne_50m_rivers_lake_centerlines") if f["properties"]["scalerank"] <= 3]
    lakes = [(f["properties"].get("name") or "", shape(f["geometry"]).buffer(0)) for f in load(d, "ne_50m_lakes")]

    countries = []
    for iso in ORDER:
        frame = FRAMES[iso]
        pivot = 1 if iso == "RUS" else None
        units = []
        for p, g in admin1[iso]:
            g = unwrap(g, pivot)
            if iso == "USA":
                ak, hi = usa_insets(g)
                g = unary_union([clip_frame(g, (-125, -66, 24, 50)), ak, hi])
            else:
                g = clip_frame(g, frame)
            if not g.is_empty: units.append((p, g))
        key = GROUPING.get(iso, "name") if len(units) > 40 or iso in GROUPING else "name"
        groups, labels, names = {}, [], []
        geoms, vals = [], []
        for p, g in units:
            gid = (p.get(key) or "") if key else ""
            if gid not in groups:
                if len(groups) >= len(ALPHABET): gid = list(groups)[-1]
                else:
                    groups[gid] = ALPHABET[len(groups)]
                    postal = (p.get("postal") or "") if key == "name" else ""
                    labels.append(postal.lower() if postal and len(postal) <= 3 else "")
                    names.append(gid if key else "")
            geoms.append(g); vals.append(groups[gid])
        r = country_raster(frame)
        grid = r.sample(geoms, vals)
        body = unary_union(geoms)

        tgeoms, tvals, tnames = [], [], Counter()
        for kind, name, g in terrain:
            gg = unwrap(g, pivot).intersection(body)
            if not gg.is_empty: tgeoms.append(gg); tvals.append(kind + name)
        for name, g in rivers:
            gg = unwrap(g, pivot).buffer(r.cw / r.xscale * 0.3).intersection(body)
            if not gg.is_empty: tgeoms.append(gg); tvals.append("r" + name)
        for name, g in lakes:
            gg = unwrap(g, pivot).intersection(body)
            if not gg.is_empty: tgeoms.append(gg); tvals.append("r" + name)
        tgrid = []
        if tgeoms:
            tree = STRtree(tgeoms)
            for cy in range(r.h):
                row = []
                for cx in range(r.w):
                    if grid[cy][cx] == " ": row.append(" "); continue
                    votes = Counter()
                    for sy in range(SUB):
                        for sx in range(SUB):
                            pt = Point(*r.lonlat(cx, cy, sx, sy))
                            for idx in tree.query(pt):
                                if tgeoms[idx].contains(pt): votes[tvals[idx]] += 1
                    pick = " "
                    for kind, need in (("r", 2), ("m", 4), ("d", 4)):
                        hits = {v: n for v, n in votes.items() if v[0] == kind}
                        if sum(hits.values()) >= need:
                            best = max(hits, key=hits.get)
                            if best[1:]: tnames[best] += 1
                            pick = kind; break
                    row.append(pick)
                tgrid.append("".join(row))
        else:
            tgrid = [" " * r.w for _ in range(r.h)]

        features = {"m": [], "d": [], "r": []}
        for v, n in tnames.most_common():
            name = v[1:]
            if name in SKIP_NAMES: continue
            if any(name[:4].lower() == f[:4].lower() for f in features[v[0]]): continue
            if n >= 2 and len(features[v[0]]) < 4:
                features[v[0]].append(v[1:])

        sites = {}
        for sid, (lon, lat) in SITES.items():
            if not sid.startswith(iso + "_"): continue
            if pivot and lon < 0: lon += 360
            cx = (lon * r.xscale - r.x0) / r.cw
            cy = (r.y0 - lat) / r.ch
            sites[sid] = (cx / r.w, cy / r.h)
        countries.append((iso, r, grid, tgrid, labels, names, features, sites))
        print(iso, r.w, r.h, len(groups), "regions by", key, features, file=sys.stderr)

    # World raster.
    wr = Raster(-180.0, WORLD_LAT_TOP, 360.0 / WORLD_W, (WORLD_LAT_TOP - WORLD_LAT_BOTTOM) / WORLD_H,
                WORLD_W, WORLD_H, 1.0)
    wgeoms, wvals = [], []
    for iso, g in admin0.items():
        if iso == "ATA": continue
        wgeoms.append(g); wvals.append(ALPHABET[ORDER.index(iso)] if iso in ORDER else ".")
    world = wr.sample(wgeoms, wvals, threshold=2)
    anchors = {}
    for iso in ORDER:
        lon, lat = SITES[iso + "_CAP"]
        anchors[iso] = ((lon + 180.0) / 360.0, (WORLD_LAT_TOP - lat) / (WORLD_LAT_TOP - WORLD_LAT_BOTTOM))

    L = []
    w = L.append
    w("// <auto-generated>")
    w("// Generated by Tools/mapgen/generate_map_atlas.py from Natural Earth")
    w("// (public domain, naturalearthdata.com). Do not edit by hand: re-run the")
    w("// generator. Presentation data only — no simulation system reads it.")
    w("// </auto-generated>")
    w("using System.Collections.Generic;")
    w("")
    w("namespace Brink.UI")
    w("{")
    w("    public static partial class MapAtlas")
    w("    {")
    w(f"        const string RegionAlphabet = {cs(ALPHABET)};")
    w(f"        const int WorldWidth = {WORLD_W};")
    w(f"        const int WorldHeight = {WORLD_H};")
    w(f"        static readonly string[] CountryOrder = {{ {', '.join(cs(c) for c in ORDER)} }};")
    w("        static readonly string[] WorldRows =")
    w("        {")
    for row in world: w(f"            {cs(rle(row))},")
    w("        };")
    w("        static readonly Dictionary<string, (float x, float y)> WorldAnchors = new Dictionary<string, (float, float)>")
    w("        {")
    for iso, (x, y) in anchors.items(): w(f"            {{ {cs(iso)}, ({x:.4f}f, {y:.4f}f) }},")
    w("        };")
    w("        static readonly Dictionary<string, (float x, float y)> WorldSites = new Dictionary<string, (float, float)>")
    w("        {")
    for sid, (lon, lat) in SITES.items():
        x, y = (lon + 180.0) / 360.0, (WORLD_LAT_TOP - lat) / (WORLD_LAT_TOP - WORLD_LAT_BOTTOM)
        w(f"            {{ {cs(sid)}, ({x:.4f}f, {y:.4f}f) }},")
    w("        };")
    w("        static readonly Dictionary<string, Chart> Charts = new Dictionary<string, Chart>")
    w("        {")
    for iso, r, grid, tgrid, labels, names, features, sites in countries:
        w(f"            {{ {cs(iso)}, new Chart({r.w}, {r.h},")
        w("                new string[] { " + ", ".join(cs(rle(x)) for x in grid) + " },")
        w("                new string[] { " + ", ".join(cs(rle(x)) for x in tgrid) + " },")
        w("                new string[] { " + ", ".join(cs(x) for x in labels) + " },")
        w("                new string[] { " + ", ".join(cs(x) for x in names) + " },")
        w("                new string[] { " + ", ".join(cs(x) for x in features["m"]) + " },")
        w("                new string[] { " + ", ".join(cs(x) for x in features["d"]) + " },")
        w("                new string[] { " + ", ".join(cs(x) for x in features["r"]) + " },")
        w("                new Dictionary<string, (float, float)> { " +
          ", ".join(f"{{ {cs(k)}, ({x:.4f}f, {y:.4f}f) }}" for k, (x, y) in sites.items()) + " }) },")
    w("        };")
    w("    }")
    w("}")
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(L) + "\n")


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    target = os.path.join(here, "..", "..", "Brink", "Assets", "Scripts", "UI", "MapAtlas.Generated.cs")
    main(sys.argv[1], os.path.normpath(target))
