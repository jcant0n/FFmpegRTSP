# Introduction
 This project contains AV funcionality for surveliance systems to record video from a camera rtsp stream and store it into small video segments. The library also provides some funcionality to merge these small chunks of AV in one single video.

# Features
- Segment rtsp streams into small video segments
- Customized merge of video segments, selecting time and duration

# Main Classes
 - RTSPStreamSegmentRecorder
 - SegmentMerger

# Usage

## Pull video from rtsp camera and segment

````Csharp
var url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
var options = new RTSPStreamSegmentRecorder.SegmentRecordOptions();

RTSPStreamSegmentRecorder.RecordSegmentedRtspStream(url: url, options);
````
## Merge video segments functionality

````Csharp
SegmentMerger.MergeSegments(
                            inputFilePatern: "Resources\\*.ts",
                            outputFilePath: "merged.ts",
                            startTime: TimeSpan.FromSeconds(7.5),
                            recordDuration: TimeSpan.FromSeconds(14),
                            options.HWDevice,
                            ignoreAudio: false,
                            defaultFps: 24
                            );
````

# Validation notes

The merge funcionality was tested to work in some set of video chunks in .ts format obtained from some real surveliance cameras. 