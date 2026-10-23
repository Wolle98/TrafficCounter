# Demo data

`TrafficCounter.DemoData` creates a database with synthetic traffic. It serves for developing and demonstrating the analysis GUI and the reports (stage 4) before real data exists. The data is entirely made up and has no reference to any site; the sun positions (day/dusk/night) are computed for rough default coordinates in Germany.

```powershell
dotnet run --project src/TrafficCounter.DemoData -c Release -- --direction-a "Direction A" --direction-b "Direction B"
```

| Option | Default |
|---|---|
| `--connection` | `Server=localhost;Database=TrafficCounter_Demo;Integrated Security=true;TrustServerCertificate=true` |
| `--days` | 21 |
| `--first-day` | today minus `--days` (i.e. up to yesterday, full days) |
| `--seed` | 42 (same seed → same data) |
| `--direction-a`, `--direction-b` | direction names of the demo camera |

The database is **deleted and recreated** (via the migrations). So that no real data is lost, the tool refuses any database whose name does not contain "Demo".

The analysis app can be pointed at it with `--ConnectionStrings:TrafficCounter="Server=localhost;Database=TrafficCounter_Demo;Integrated Security=true;TrustServerCertificate=true"`.

## Model

The model uses synthetic, realistically chosen example values for a residential street in a 30 km/h zone.

**Traffic volume** (vehicles per hour, all classes, ± daily and hourly variation, arrivals as a Poisson process):

- **Working days,** about 3,400 vehicles per day:
  - morning peak 07:30–08:00 at about 390/h;
  - school traffic 13:00–14:30 at 260–270/h;
  - evening peak 16:30–17:00 at about 315/h;
  - otherwise 150–250/h during the day;
  - in the evening 180/h at 7 pm, 60/h at 9 pm, 30/h at 11 pm.
- **Night before 7 am:** 5–20/h, rising from 5 am, 06:30–07:00 about 160/h. Somewhat more on Friday evening.
- **Weekend:** no peaks, 120–200/h during the day, Saturday active earlier than Sunday.

**Classes** over the whole period: car 82 %, van 4.5 %, truck 1.5 %, agricultural 0.2 %, motorcycle 1.5 %, bicycle/e-scooter 10 %. The distribution over the day differs per class; the shares are still preserved:

- **Buses run to an example timetable:**
  - on working days 6 am–8 pm one per hour in direction A at about minute :15 and one in direction B at about minute :45, ±2 minutes each;
  - at the weekend every two hours 8 am–7 pm.
- Bicycles during the day and early evening, more at school times, hardly at night.
- Tractors 7 am–8 pm, hardly on Sundays.
- Trucks: hardly on Sundays (driving ban), fewer at night.
- Vans fewer at the weekend.
- Motorcycles more during the day at the weekend.

**Speed** (30 km/h limit):

- Motor vehicles overall: 80 % 28–35 km/h, 10 % 35–45, 5 % 45–55, 5 % 55–70.
  - Speeding is clearly more frequent at night, the more so the higher.
  - Cars, vans and motorcycles speed more often than trucks and buses.
  - At the school peaks speeding is less frequent.
- Bicycles/e-scooters: 85 % 15–20 km/h, 12 % 20–25, 3 % 10–15.
- Tractors: 15–40 km/h, mostly around 25. They do not count towards the 30 km/h bands.

Note: on many 30 km/h streets the speeds are lower (median about 30 km/h, hardly any above 50 km/h); the demo data keeps a wider spread to make the speed views interesting.

**Trailers:** tractors pull one trailer in 45 % and two in 15 % of cases, trucks one in 8 %, cars and vans one in 0.15 %.

**Direction:** direction A has 44 % on average (39–50 % per hour), about ±2 % per day.

**Position on the line:**

- As a fraction of the counting line from property boundary to property boundary with the kerbs at 0.14 and 0.67:
  - cars in direction A (far lane) around 0.37, in direction B (near lane) around 0.44, ±0.05 each. Evading parked cars is included.
  - Large vehicles drive somewhat more centrally.
- 18 % of the cyclists ride on one of the footpaths, the rest at the edge of their lane.

**Operation:** the minute statistics, drift measurements every 5 min and system events are generated as well. On fixed days there is also one incident of each kind:

| Day | Incident |
|---|---|
| 5 | restart for an update (2 min pause, clean end) |
| 10 | crash at 03:41, restart at 06:55 (unclean end) |
| 12 | camera moved after window cleaning (09:30–18:00) |
| 14 | camera unavailable for 28 min |
| 17 | rain: quality limited 3–6 pm, unusable 16:10–16:40 (about 40 % of the vehicles missing) |
