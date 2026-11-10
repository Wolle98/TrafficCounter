# ADR-0063: No speed from a stalled stream

- **Status:** accepted
- **Date:** 2026-11-10
- **Stage:** 5

## Context

Every frame carries the time at which the capture thread received it (architecture: "every frame carries its own timestamp"). The speed is a line fitted through the positions over these timestamps (ADR-0015, ADR-0056). This is only right if a frame arrives when it was taken.

The network camera's stream sometimes falls behind for seconds to half a minute. Its frames then arrive late and fewer per second, and afterwards come in a rush until the stream has caught up. Over the minute the number of frames stays normal. Each frame still shows the next moment of the scene, but its timestamp is too late, so the timestamps no longer match the moments shown:

- While the stream falls behind, a vehicle seems slower. In a recording, the source delivered 18 instead of 25 frames per second for 30 s. A car in that stretch was measured at 24.9 km/h, while the reference camera measured 40.9 km/h. Its passage time was also 4 s late.
- While the stream catches up, a vehicle seems faster. In another case a car read 70.2 km/h against 31.7 km/h from the reference camera, and the car just before it read 22.2 km/h against 41.2.

Neither the hourly median nor the minute statistics show this. Speed clips do not show it either, because they store the same timestamps.

## Options considered

1. **Discard the speed** when the source did not deliver frames at its normal rate while the vehicle was in the zone.
2. **Timestamps from the frame count** (sequence number divided by the nominal rate) instead of the arrival time. This is right only as long as the camera never drops a frame, and it needs a nominal rate per source. The USB camera slows down at night (longer exposure).
3. **Timestamps from the stream** (RTP/PTS). OpenCV does not reliably give them for RTSP. They also depend on the camera clock and restart on every reconnect.

## Decision

We choose option 1, because it needs no assumption about the camera and costs few speeds.

- The source numbers every frame it receives (`Frame.Sequence`). The pipeline hands this number to the counting with every frame, including the frames the motion gate skips.
- `CaptureRate` takes the rate of every ten-second stretch over the last five minutes. The median of these rates is the normal rate. A stall of half a minute hardly moves it, and a camera that slows down at night is followed within minutes. Stretches longer than 30 s, which span a reconnect, are left out. A new source (falling numbers) starts over. The rate is known after 30 s; before that nothing is checked.
- At the passage, the frames received between the first and the last position in the speed zone, per second, are compared with the normal rate. If the ratio deviates by more than 15 % (`SpeedSettings.MaxCaptureRateDeviation`), the passage gets no speed. It is still counted, and its mean speed still tells a vehicle from a pedestrian (ADR-0050).
- The ratio is part of the passage's track observation and is logged with every passage. A discarded speed is logged at information level.

In four reference recordings of one hour, the rate over windows of 0.7–1.5 s stayed within ±5 % of the normal rate in 98 % of the windows. Only the recording with the stall had more than 0.7 % of windows beyond ±15 %. In the replay of that stall the car above got no speed (ratio 0.67); the cars before it had 0.99.

## Consequences

- Positive: single speeds that were far too high or too low because of a stalled stream are no longer stored. Counting is unchanged.
- Negative / risks: a camera that drops frames instead of delaying them would lose speeds without need. The frame counts per minute stay at 1,499–1,501 except around stalls, so this is rare. Replays of short clips are not checked (no normal rate after 7 s).
- Follow-up work: count discarded speeds per day from the service log. If they are frequent, look at option 2 again.
