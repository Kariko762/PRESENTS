"""Generates the Data Lab demo data sets deterministically.

Run from the repository root:  python tools/DataGenerators/data_lab.py
Writes samples/Demos/DataLab/data/** . Re-running gives identical files (fixed seeds).

Layout produced
  data/tier1-bank.json        default, standalone (~400 trades)          -> $files.history = data/shared/trade-history.csv
  data/regional-broker.json   $extends tier1-bank: EUR / de-DE, own trade list
  data/asset-manager.json     $extends tier1-bank: client, persona and KPIs only
  data/stress-test.json       standalone, close to the 1 MB inline limit -> $files.history = data/stress/trade-history-100k.csv
  data/edge-cases.json        standalone: empty arrays, nulls, long text, unicode, negatives, missing fields
"""
import csv
import json
import os
import random
from datetime import date, datetime, timedelta

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "samples", "Demos", "DataLab", "data")

DESKS = [
    ("EQ-CASH", "Equities Cash", ["EQ-LDN-01", "EQ-NY-02", "EQ-HK-03"]),
    ("FI-RATES", "Rates", ["RT-GOV-01", "RT-SWAP-02"]),
    ("FI-CREDIT", "Credit", ["CR-IG-01", "CR-HY-02"]),
    ("FX", "FX & Money Markets", ["FX-SPOT-01", "FX-FWD-02"]),
]
INSTRUMENTS = [
    # (ticker, isin, asset class, desk, price, lot)
    ("AAPL", "US0378331005", "Equity", "EQ-CASH", 228.4, 100),
    ("MSFT", "US5949181045", "Equity", "EQ-CASH", 431.2, 100),
    ("NVDA", "US67066G1040", "Equity", "EQ-CASH", 138.9, 100),
    ("VOD", "GB00BH4HKS39", "Equity", "EQ-CASH", 0.72, 5000),
    ("HSBA", "GB0005405286", "Equity", "EQ-CASH", 7.05, 1000),
    ("SAP", "DE0007164600", "Equity", "EQ-CASH", 214.6, 100),
    ("0700", "KYG875721634", "Equity", "EQ-CASH", 412.0, 100),
    ("UST 4.25 2034", "US91282CKQ32", "Government Bond", "FI-RATES", 98.42, 1_000_000),
    ("GILT 4.5 2034", "GB00BMV7TC88", "Government Bond", "FI-RATES", 101.15, 1_000_000),
    ("BUND 2.6 2034", "DE000BU2Z031", "Government Bond", "FI-RATES", 100.6, 1_000_000),
    ("USD 10Y IRS", "", "Interest Rate Swap", "FI-RATES", 0.0, 10_000_000),
    ("JPM 5.0 2031", "US46647PDY92", "Corporate Bond", "FI-CREDIT", 101.9, 500_000),
    ("VW 4.25 2030", "XS2554488978", "Corporate Bond", "FI-CREDIT", 99.3, 500_000),
    ("FORD 6.1 2032", "US345370DB39", "High Yield Bond", "FI-CREDIT", 97.1, 250_000),
    ("EUR/USD", "", "FX Spot", "FX", 1.0842, 1_000_000),
    ("GBP/USD", "", "FX Spot", "FX", 1.2715, 1_000_000),
    ("USD/JPY", "", "FX Forward", "FX", 151.32, 1_000_000),
]
COUNTERPARTIES = ["Northbridge Securities", "Atlas Capital Markets", "Meridian Bank", "Harbour Global Investors",
                  "Kestrel Asset Management", "Union Pacifica Bank", "Crestline Partners", "Solent Clearing"]
VENUES = ["XNYS", "XNAS", "XLON", "XETR", "XHKG", "MTS", "Tradeweb", "Bloomberg", "OTC"]
STATUSES = [("Settled", 62), ("Cleared", 12), ("Matched", 9), ("Pending", 8), ("Exception", 6), ("Cancelled", 3)]
STAGES = ["Captured", "Enriched", "Matched", "Cleared", "Settled"]
EXCEPTION_TYPES = [("Price tolerance", "Medium"), ("Missing SSI", "High"), ("Quantity mismatch", "High"),
                   ("Late affirmation", "Low"), ("Counterparty not found", "High"), ("Fail to deliver", "Critical")]
OWNERS = ["M. Okafor", "J. Lindqvist", "P. Raman", "A. Moreau", "S. Tanaka"]


def weighted(rng, items):
    total = sum(weight for _, weight in items)
    pick = rng.uniform(0, total)
    for value, weight in items:
        pick -= weight
        if pick <= 0:
            return value
    return items[-1][0]


def business_days(end, count):
    days, current = [], end
    while len(days) < count:
        if current.weekday() < 5:
            days.append(current)
        current -= timedelta(days=1)
    return list(reversed(days))


def notional_of(quantity, price, asset, ticker=""):
    if "Bond" in asset:
        return round(quantity * price / 100, 2)
    if asset == "Interest Rate Swap":
        return float(quantity)
    if asset.startswith("FX") and ticker.startswith("USD/"):
        return float(quantity)  # USD base amount; the rate converts into the quote currency
    return round(quantity * price, 2)


def make_trades(rng, count, as_of, prefix="T"):
    trades = []
    window = business_days(as_of, 20)
    for index in range(count):
        ticker, isin, asset, desk, price, lot = rng.choice(INSTRUMENTS)
        books = next(d[2] for d in DESKS if d[0] == desk)
        trade_date = rng.choice(window)
        settle = trade_date + timedelta(days=1 if asset in ("Equity", "Government Bond") else 2)
        quantity = lot * rng.randint(1, 40)
        traded = round(price * (1 + rng.uniform(-0.01, 0.01)), 4 if asset.startswith("FX") else 2)
        status = weighted(rng, STATUSES)
        if trade_date >= window[-2] and status == "Settled":
            status = "Matched"
        trades.append({
            "id": f"{prefix}-{48000 + index}",
            "tradeDate": trade_date.isoformat(),
            "settleDate": settle.isoformat(),
            "desk": desk,
            "book": rng.choice(books),
            "side": rng.choice(["BUY", "SELL"]),
            "instrument": ticker,
            "isin": isin,
            "assetClass": asset,
            "quantity": quantity,
            "price": traded,
            "notional": notional_of(quantity, traded, asset, ticker),
            "counterparty": rng.choice(COUNTERPARTIES),
            "venue": "OTC" if asset in ("Interest Rate Swap", "FX Forward") else rng.choice(VENUES),
            "status": status,
        })
    return trades


def make_positions(rng, trades):
    book_totals = {}
    for trade in trades:
        if trade["status"] == "Cancelled":
            continue
        key = (trade["desk"], trade["book"], trade["instrument"], trade["assetClass"])
        signed = 1 if trade["side"] == "BUY" else -1
        current = book_totals.setdefault(key, {"quantity": 0, "marketValue": 0.0})
        current["quantity"] += signed * trade["quantity"]
        current["marketValue"] += signed * trade["notional"]
    positions = []
    for (desk, book, instrument, asset), totals in sorted(book_totals.items()):
        market_value = round(totals["marketValue"], 2)
        positions.append({
            "desk": desk, "book": book, "instrument": instrument, "assetClass": asset,
            "quantity": totals["quantity"], "marketValue": market_value,
            "pnl": round(abs(market_value) * rng.uniform(-0.03, 0.035), 2),
        })
    return positions


def make_series(rng, as_of, days=260, scale=1.0):
    dates = business_days(as_of, days)
    pnl, var, volume = [], [], []
    for _ in dates:
        pnl.append(round(rng.gauss(0.25, 1.0) * 650_000 * scale))
        var.append(round(abs(rng.gauss(2.4, 0.35)) * 1_000_000 * scale))
        volume.append(int(rng.gauss(1800, 260) * scale))
    return {"dates": [d.isoformat() for d in dates], "pnl": pnl, "var": var, "volume": volume}


def make_exceptions(rng, trades, count):
    candidates = [t for t in trades if t["status"] in ("Exception", "Pending", "Matched")] or trades
    exceptions = []
    for index, trade in enumerate(rng.sample(candidates, min(count, len(candidates)))):
        kind, severity = rng.choice(EXCEPTION_TYPES)
        raised = datetime.fromisoformat(trade["tradeDate"] + "T09:00:00") + timedelta(minutes=rng.randint(5, 480))
        exceptions.append({
            "id": f"EX-{7100 + index}", "tradeId": trade["id"], "type": kind, "severity": severity,
            "raised": raised.isoformat(timespec="minutes"), "owner": rng.choice(OWNERS),
            "ageHours": rng.randint(1, 70), "note": f"{kind} on {trade['instrument']} with {trade['counterparty']}.",
        })
    return exceptions


def make_events(rng, trades, exceptions, count):
    flagged = {e["tradeId"] for e in exceptions}
    events = []
    for trade in [t for t in trades if t["id"] in flagged][:count]:
        at = datetime.fromisoformat(trade["tradeDate"] + "T09:30:00") + timedelta(minutes=rng.randint(0, 300))
        reached = STAGES.index("Settled") if trade["status"] == "Settled" else rng.randint(1, 3)
        for stage in STAGES[: reached + 1]:
            events.append({"tradeId": trade["id"], "at": at.isoformat(timespec="seconds"), "stage": stage,
                           "detail": f"{stage} · {trade['side']} {trade['quantity']:,} {trade['instrument']}"})
            at += timedelta(minutes=rng.randint(1, 90))
    return events


def kpis(trades, exceptions, scale=1.0):
    live = [t for t in trades if t["status"] != "Cancelled"]
    settled = [t for t in live if t["status"] == "Settled"]
    return {
        "tradesToday": int(len(live) * 31 * scale),
        "notional": round(sum(t["notional"] for t in live) * 18 * scale),
        "stpRate": round(0.91 + (len(settled) / max(1, len(live))) * 0.08, 4),
        "openBreaks": len(exceptions),
        "settledOnTime": round(0.982 - len(exceptions) / 4000, 4),
        "avgMatchMinutes": round(4.2 + len(exceptions) / 40, 1),
    }


def write_csv(path, rng, rows, as_of):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    window = business_days(as_of, 500)
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["id", "tradeDate", "desk", "side", "instrument", "assetClass", "quantity", "price", "notional", "counterparty", "status"])
        for index in range(rows):
            ticker, _, asset, desk, price, lot = rng.choice(INSTRUMENTS)
            quantity = lot * rng.randint(1, 40)
            traded = round(price * (1 + rng.uniform(-0.04, 0.04)), 4 if asset.startswith("FX") else 2)
            writer.writerow([f"H-{100000 + index}", rng.choice(window).isoformat(), desk, rng.choice(["BUY", "SELL"]), ticker, asset,
                             quantity, traded, notional_of(quantity, traded, asset, ticker), rng.choice(COUNTERPARTIES), weighted(rng, STATUSES)])
    print(f"{os.path.relpath(path, ROOT):28} {os.path.getsize(path) / 1024:8.1f} KB  ({rows:,} rows)")


def dump(name, body):
    """Top level indented, one table row per line: readable in the Studio editor and far smaller than fully indented JSON."""
    lines = ["{"]
    items = list(body.items())
    for position, (key, value) in enumerate(items):
        comma = "," if position < len(items) - 1 else ""
        if isinstance(value, list) and value and isinstance(value[0], dict):
            rows = [f"    {json.dumps(row, ensure_ascii=False, separators=(',', ':'))}" for row in value]
            lines.append(f"  {json.dumps(key)}: [\n" + ",\n".join(rows) + f"\n  ]{comma}")
        elif isinstance(value, dict) and any(isinstance(v, list) and len(v) > 20 for v in value.values()):
            inner = [f"    {json.dumps(k)}: {json.dumps(v, ensure_ascii=False, separators=(',', ':'))}" for k, v in value.items()]
            lines.append(f"  {json.dumps(key)}: {{\n" + ",\n".join(inner) + f"\n  }}{comma}")
        else:
            text = json.dumps(value, ensure_ascii=False, indent=2).replace("\n", "\n  ")
            lines.append(f"  {json.dumps(key)}: {text}{comma}")
    lines.append("}")
    path = os.path.join(ROOT, name)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")
    print(f"{name:28} {os.path.getsize(path) / 1024:8.1f} KB")


def main():
    as_of = date(2026, 10, 30)

    rng = random.Random(4801)
    trades = make_trades(rng, 400, as_of)
    exceptions = make_exceptions(rng, trades, 24)
    dump("tier1-bank.json", {
        "client": {"name": "Northgate Global Bank", "shortName": "Northgate", "segment": "Tier 1 bank", "region": "North America", "lei": "5493001KJTIIGC8Y1R12"},
        "persona": "Head of Trade Operations",
        "locale": "en-US",
        "currency": "USD",
        "asOf": as_of.isoformat(),
        "headline": "Every trade, every stage, one view.",
        "kpis": kpis(trades, exceptions),
        "desks": [{"id": d[0], "name": d[1], "books": d[2]} for d in DESKS],
        "trades": trades,
        "positions": make_positions(rng, trades),
        "series": make_series(rng, as_of),
        "exceptions": exceptions,
        "events": make_events(rng, trades, exceptions, 12),
        "$files": {"history": "data/shared/trade-history.csv"},
    })
    write_csv(os.path.join(ROOT, "shared", "trade-history.csv"), random.Random(77), 20_000, as_of)

    rng = random.Random(9902)
    broker_trades = make_trades(rng, 120, as_of, prefix="RB")
    broker_exceptions = make_exceptions(rng, broker_trades, 9)
    dump("regional-broker.json", {
        "$extends": "data/tier1-bank.json",
        "client": {"name": "Rheinwerk Wertpapierhandel AG", "shortName": "Rheinwerk", "segment": "Regional broker", "region": "DACH", "lei": "529900T8BM49AURSDO55"},
        "persona": "Leiter Wertpapierabwicklung",
        "locale": "de-DE",
        "currency": "EUR",
        "headline": "Weniger Brüche, schnellere Abwicklung.",
        "kpis": kpis(broker_trades, broker_exceptions, 0.25),
        "trades": broker_trades,
        "positions": make_positions(rng, broker_trades),
        "exceptions": broker_exceptions,
        "events": make_events(rng, broker_trades, broker_exceptions, 6),
    })

    dump("asset-manager.json", {
        "$extends": "data/tier1-bank.json",
        "client": {"name": "Kestrel Asset Management", "shortName": "Kestrel", "segment": "Asset manager"},
        "persona": "COO, Investment Operations",
        "locale": "en-GB",
        "currency": "GBP",
        "headline": "Confidence from order to settlement.",
        "kpis": {"tradesToday": 2140, "openBreaks": 6},
    })

    rng = random.Random(3303)
    stress_trades = make_trades(rng, 2600, as_of, prefix="S")
    stress_exceptions = make_exceptions(rng, stress_trades, 180)
    dump("stress-test.json", {
        "client": {"name": "Stress Test Holdings", "shortName": "Stress", "segment": "Synthetic", "region": "Global"},
        "persona": "Platform engineering",
        "locale": "en-US",
        "currency": "USD",
        "asOf": as_of.isoformat(),
        "headline": "How far can inline data go?",
        "kpis": kpis(stress_trades, stress_exceptions, 4),
        "desks": [{"id": d[0], "name": d[1], "books": d[2]} for d in DESKS],
        "trades": stress_trades,
        "positions": make_positions(rng, stress_trades),
        "series": make_series(rng, as_of, days=1000, scale=4),
        "exceptions": stress_exceptions,
        "events": make_events(rng, stress_trades, stress_exceptions, 60),
        "$files": {"history": "data/stress/trade-history-100k.csv"},
    })
    write_csv(os.path.join(ROOT, "stress", "trade-history-100k.csv"), random.Random(88), 100_000, as_of)

    rng = random.Random(5505)
    edge_trades = make_trades(rng, 6, as_of, prefix="E")
    edge_trades[0]["counterparty"] = "Société Générale – Zürich Branch (测试) " + "very long legal entity name " * 4
    edge_trades[1]["notional"] = -12_500_000.5
    edge_trades[2]["price"] = None
    edge_trades[3]["status"] = ""
    del edge_trades[4]["settleDate"]
    edge_trades[5]["quantity"] = 0
    dump("edge-cases.json", {
        "client": {"name": "Ünïcødé & <Script> Test \"Quotes\" Ltd", "shortName": None},
        "persona": "QA · edge cases",
        "locale": "fr-FR",
        "currency": "CHF",
        "asOf": as_of.isoformat(),
        "headline": "",
        "kpis": {"tradesToday": 0, "notional": -1250000, "stpRate": None, "openBreaks": 0},
        "desks": [],
        "trades": edge_trades,
        "positions": [],
        "series": {"dates": [], "pnl": [], "var": [], "volume": []},
        "exceptions": [],
        "events": [],
    })


if __name__ == "__main__":
    main()
