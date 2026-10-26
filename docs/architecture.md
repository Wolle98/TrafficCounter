# Architecture

## Processing pipeline

```mermaid
flowchart LR
    CAM[IP cam RTSP / USB / video file] --> SRC[IFrameSource<br/>capture thread, latest frame]
    SRC --> PRE[Pre-processing<br/>ROI mask, letterbox]
    PRE --> DET[IObjectDetector<br/>YOLO / ONNX Runtime<br/>DirectML, OpenVINO, TensorRT/CUDA or CPU]
    DET --> TRK[ITracker<br/>ByteTrack]
    TRK --> CNT[CountingEngine<br/>counting line, direction,<br/>class voting, speed]
    CNT --> SINK[BufferedStoreWriter<br/>buffered, offline file]
    SINK --> DB[(SQL Server or<br/>PostgreSQL on the NAS)]
    CNT -.-> DBG[Debug overlay<br/>optional]
    SRC -.-> REC[ScheduledRecorder<br/>reference recordings on a schedule]
    CNT -.-> SMP[SampleCollector<br/>anonymised training samples]
    WX[Bright Sky / DWD] --> WW[WeatherWorker<br/>hourly] --> DB
    DB --> GUI[TrafficCounter.App<br/>Blazor Server]
```

The pipeline runs in a `BackgroundService`. Next to it, the service runs:

- the weather import (`WeatherWorker`, [ADR-0023](adr/0023-weather-and-image-quality.md));
- on request, reference recordings on a schedule (`ScheduledRecorder`, [ADR-0032](adr/0032-recordings-by-the-service.md)). They get a copy of the camera frames; counting never waits for them;
- on request, the collection of anonymised training samples (`SampleCollector`, [ADR-0026](adr/0026-targeted-collection-of-training-samples.md)), limited in time and per day.

Capture and processing are decoupled: the capture thread reads the stream continuously and keeps only the latest frame. If detection is slower than the camera, frames are discarded instead of buffered, so no latency can build up. Every frame carries its own timestamp; this keeps tracking and speed correct even when frames are missing.

## Complete chain in detail

From the camera to the analysis app, with the class or tool that does each step. Dashed lines are side paths that never hold up the counting.

```mermaid
flowchart TD
    subgraph CAPTURE["1 Capture (capture thread)"]
        IPCAM["IP camera<br/>H.264 over RTSP/TCP, 25 fps"] --> RTSP["RtspFrameSource<br/>OpenCV + FFmpeg, reconnect with backoff"]
        USBCAM["USB camera<br/>UVC"] --> USB["UsbFrameSource<br/>OpenCV VideoCapture, exposure/focus control"]
        FILE["Video file<br/>reference recording"] --> FS["FileFrameSource<br/>real time or as fast as possible"]
        RTSP --> BUF["LatestFrameBuffer<br/>only the newest frame, older ones dropped"]
        USB --> BUF
        FS --> BUF
    end

    subgraph FRAME["2 Per frame (counting thread, CountingPipeline)"]
        BUF --> FPS{"MaxFps<br/>e.g. 15 fps"}
        FPS -- "processed" --> BAND["Band crop<br/>BandTop/BandBottom/BandLeft/BandRight<br/>of the site configuration"]
        BAND --> GATE{"MotionGate<br/>motion in the band?<br/>at most MaxSkipSeconds without detection"}
        GATE -- "no motion" --> SKIP["Detector skipped"]
        GATE -- "motion" --> ENH["ImageEnhancer<br/>optional: CLAHE, gamma, grayscale"]
    end

    subgraph INFER["3 Detection (YoloOnnxDetector)"]
        ENH --> LB["Letterbox + resize<br/>to model input, e.g. 640x192<br/>Linear or Area"]
        LB --> ORT["ONNX Runtime<br/>YOLO11 model (.onnx)"]
        ORT --> EP{"Execution provider<br/>chosen by the build"}
        EP --> OV["OpenVINO<br/>Intel GPU/NPU/CPU (notebook)<br/>-p:InferenceBackend=OpenVino"]
        EP --> DML["DirectML<br/>any DirectX 12 GPU (PC)<br/>default build"]
        EP --> TRT["TensorRT, then CUDA<br/>NVIDIA GPU<br/>-p:InferenceBackend=Nvidia"]
        EP --> CPU["CPU<br/>fallback of every build"]
        OV --> POST["Post-processing<br/>confidence threshold, NMS,<br/>model class -> vehicle class<br/>(COCO or own model; Trailer unmapped)"]
        DML --> POST
        TRT --> POST
        CPU --> POST
        POST --> IGN["Ignore zones<br/>parked vehicles, judged by ground point"]
    end

    subgraph TRACK["4 Tracking and counting"]
        IGN --> BT["ByteTracker<br/>Kalman filter, two-stage matching,<br/>day/night thresholds, track stitching"]
        BT --> CE["CountingEngine<br/>counting line, direction A/B,<br/>class vote over the track,<br/>trailers (never two-wheelers),<br/>speed from the scale at the line"]
        CE --> DUP{"Same vehicle again?<br/>motor vehicle within 0.4 s,<br/>same direction and lane"}
        DUP -- "no" --> PAS["Passage<br/>time, direction, class, speed, lighting"]
        DUP -- "yes" --> DROP["not counted<br/>(long vehicle split into two tracks)"]
    end

    subgraph WATCH["5 Monitoring per minute (CountingWorker)"]
        WD["SourceWatchdog<br/>frame rate, black or stalled stream"]
        PANE["PaneMonitor<br/>raindrops on the window pane at night"]
        QM["QualityMonitor<br/>brightness, contrast, sharpness"]
        DRIFT["DriftMonitor<br/>camera moved?"]
        NET["NetworkProbe<br/>round trip to the router"]
        MIN["MinuteStatisticsCollector<br/>frames, passages, source state Ok/Degraded"]
        WD --> MIN
        PANE --> MIN
        QM --> MIN
    end

    subgraph STORE["6 Storage"]
        PAS --> BSW["BufferedStoreWriter<br/>batches, fallback file if the DB is unreachable"]
        MIN --> BSW
        DRIFT --> BSW
        BSW --> PG[("PostgreSQL on the NAS<br/>main database")]
        BSW --> SQL[("SQL Server<br/>PC, on demand")]
        WX["Bright Sky / DWD"] --> WW["WeatherWorker<br/>hourly"] --> BSW
        LOG["Log file<br/>Serilog, one line per minute and passage"]
    end
    NET --> LOG
    MIN --> LOG

    subgraph SIDE["Side paths (copies of the frames)"]
        REC["ScheduledRecorder<br/>reference videos by schedule.json, H.264"]
        SMP["SampleCollector<br/>anonymised training images,<br/>person model only on sampled frames"]
    end
    BUF -.-> REC
    PAS -.-> SMP

    CSW["CameraSceneWorker<br/>daily 03:05: day/night scene times<br/>from sunrise and sunset (ISAPI)"] -.-> IPCAM

    subgraph OFFLINE["7 Tools and analysis"]
        SYNC["TrafficCounter.Sync<br/>two-way sync, fill-degraded, corrections"]
        FILL["Sync-FillDegraded.ps1<br/>hourly task: sync, fill, sync"]
        CAM2[("Second camera's database<br/>same street, same model")]
        APP["TrafficCounter.App<br/>Blazor Server, analysis"]
        REG["TrafficCounter.Regression<br/>reference videos against hand counts"]
        ANN["TrafficCounter.Annotator<br/>hand counts"]
        LS["Label Studio<br/>labelling the training images"]
    end
    PG <--> SYNC <--> SQL
    FILL --> SYNC
    CAM2 -. "impaired minutes" .-> SYNC
    PG --> APP
    SQL -.-> APP
    REC -.-> ANN -.-> REG
    REC -.-> REG
    SMP -.-> LS
```

Step 2 and the motion gate are described in [ADR-0029](adr/0029-computing-load.md), the band edges in [ADR-0040](adr/0040-band-sides.md), the execution providers in [ADR-0007](adr/0007-hardware-acceleration.md), [ADR-0029](adr/0029-computing-load.md) and [ADR-0041](adr/0041-tensorrt-backend.md), trailers, two-wheelers and split tracks in [ADR-0013](adr/0013-vehicle-classes.md) and [ADR-0046](adr/0046-two-wheelers-and-split-tracks.md), the camera scene times in [ADR-0042](adr/0042-camera-scene-times.md), the side paths in [ADR-0026](adr/0026-targeted-collection-of-training-samples.md) and [ADR-0032](adr/0032-recordings-by-the-service.md), the databases and the fill from a second camera in [ADR-0030](adr/0030-postgresql-and-synchronisation.md) and [operations.md](operations.md).

## Project structure (public repo)

```
TrafficCounter/
├─ docs/                       roadmap, architecture, guides, ADRs
├─ src/
│  ├─ TrafficCounter.Core/            domain model, geometry, image-independent interfaces (ITracker, ITrafficStore), operations log
│  ├─ TrafficCounter.Vision/          OpenCvSharp: frame sources, IObjectDetector, recording, anonymisation, measurements
│  ├─ TrafficCounter.Detection.Onnx/  YOLO inference with ONNX Runtime (DirectML/CPU, or OpenVINO as a separate build)
│  ├─ TrafficCounter.Tracking/        ByteTrack, Kalman filter, track stitching
│  ├─ TrafficCounter.Counting/        counting line, direction, class voting, trailers, speed
│  ├─ TrafficCounter.Pipeline/        processing per frame (detection → tracking → counting), site geometry, preview
│  ├─ TrafficCounter.Data/            EF Core (SQL Server and PostgreSQL), migrations, synchronisation, weather import
│  ├─ TrafficCounter.Evaluation/      ground truth format, comparison count ↔ ground truth, speed test drives
│  ├─ TrafficCounter.Service/         worker service (console + Windows service), scheduled recordings, sample collection
│  ├─ TrafficCounter.CameraProbe/     tool: check camera capabilities, live view, record and compress reference videos
│  ├─ TrafficCounter.Annotator/       tool: count reference videos by key press (ground truth), mark speed test drives
│  ├─ TrafficCounter.Calibrator/      tool: set band, counting line, kerbs, static zones, ignore zones (later homography)
│  ├─ TrafficCounter.Regression/      tool: evaluate reference videos against ground truth, live comparison, speed calibration and test drives
│  ├─ TrafficCounter.Sync/            tool: set up PostgreSQL, synchronise databases, load weather, rescale speeds
│  ├─ TrafficCounter.DemoData/        tool: fill a demo database with synthetic traffic
│  └─ TrafficCounter.App/             analysis app (stage 4, Blazor Server): overview, period, weekly and daily pattern, speed, operations
├─ tests/
│  └─ TrafficCounter.*.Tests/         unit, scenario and end-to-end tests (xUnit v3); SQL integration tests with TRAFFICCOUNTER_TEST_DB
├─ config/
│  └─ site.example.json               template of the private site configuration
└─ tools/
   ├─ deploy/                         packages and installation scripts for service and app (Windows services)
   ├─ setup/                          script that creates the private data repository
   └─ training/                       Python scripts: export to ONNX, quantisation, later fine-tuning
```

Projects are only created once they get content. Interfaces that contain image data (`Mat`), i.e. `IFrameSource` and `IObjectDetector`, are in `TrafficCounter.Vision`.

Dependencies only point inwards: `Core` knows neither OpenCV nor ONNX nor EF Core. This makes it possible to test counting and tracking logic without a camera and without a GPU.

## Private repo

```
TrafficCounterPrivate/
├─ config/        site-specific configuration (counting line, ROI, scale, direction names), reference notes
├─ recordings/    reference videos + ground truth counts (Git LFS)
├─ datasets/      training images and labels (Git LFS), inbox of collected samples
├─ models/        models (.pt/.onnx, Git LFS)
├─ db/            backups of the real database (Git LFS)
└─ reports/       generated analyses
```

`tools/setup/New-PrivateRepository.ps1` creates this structure. The service finds the private configuration via the environment variable `TRAFFICCOUNTER_CONFIG` (path to a JSON file). **Secrets** such as the RTSP password or the connection string are not checked in, not even in the private repo. They are in user secrets or environment variables.

## Data model (forward-compatible up to stage 5)

```mermaid
erDiagram
    Camera ||--o{ VehiclePassage : records
    Camera ||--o{ OperatingPeriod : runs
    OperatingPeriod ||--o{ MinuteStatistic : "one per minute"
    OperatingPeriod ||--o{ SystemEvent : logs
    VehicleClass ||--o{ VehiclePassage : classifies
    Camera {
        int Id PK
        nvarchar Name
        nvarchar DirectionAName
        nvarchar DirectionBName
    }
    VehiclePassage {
        bigint Id PK
        int CameraId FK
        datetimeoffset PassedAt
        tinyint Direction "0=unknown 1=A 2=B"
        tinyint VehicleClassId FK "stage 3, null before"
        real ClassConfidence
        real SpeedKmh "stage 5, null before"
        real LinePosition "0..1 along the counting line (lane/footpath)"
        int TrackLengthFrames
        int OperatingPeriodId FK
    }
    OperatingPeriod {
        int Id PK
        int CameraId FK
        datetimeoffset StartedAt
        datetimeoffset LastHeartbeatAt
        datetimeoffset EndedAt
        tinyint EndReason "Shutdown/Unclean/Error"
        real NominalFps
        nvarchar AppVersion
        nvarchar ModelName
        nvarchar ConfigHash
    }
    VehicleClass {
        tinyint Id PK
        nvarchar Name
    }
    MinuteStatistic {
        bigint Id PK
        datetimeoffset MinuteStart
        int FramesCaptured
        int FramesProcessed
        int FramesDropped
        int Detections
        int Passages
        tinyint SourceState
        tinyint Quality
        real Brightness
        real Contrast
        real Sharpness
        tinyint Lighting
    }
    SystemEvent {
        bigint Id PK
        datetimeoffset OccurredAt
        tinyint Kind
        tinyint Severity
        nvarchar Message
    }
```

The diagram shows the core tables. In addition there are `DriftMeasurement` (camera position, [ADR-0019](adr/0019-camera-position-monitoring.md)) and `WeatherHour` (hourly weather, [ADR-0023](adr/0023-weather-and-image-quality.md)); passages also carry lighting, trailer count, source and track details, and all synchronised rows carry `SyncId`/`UpdatedAt` ([ADR-0030](adr/0030-postgresql-and-synchronisation.md)).

`OperatingPeriod`, `MinuteStatistic` and `SystemEvent` answer for every minute whether the system was off, ran but could not evaluate anything, or ran and there really was no traffic ([ADR-0024](adr/0024-operations-log-and-logging.md)). Without this information the statistics in stage 4 are not reliable.
