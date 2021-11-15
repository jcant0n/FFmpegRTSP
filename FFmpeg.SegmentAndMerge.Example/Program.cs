using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FFmpeg.SegmentAndMerge.Example
{
    public static class Program
    {
        private static void Main(string[] args)
        {
            RTSPStreamSegmentRecorder.Initialize();

            //var url = "http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4"; // be advised this file holds 1440 frames
            var url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
            var options = new RTSPStreamSegmentRecorder.SegmentRecordOptions();
            
            // ---------------- FUNCIONALIDAD SEGMENTAR VIDEO ----------------
            //RTSPStreamSegmentRecorder.RecordSegmentedRtspStream(url: url, options);

            // ---------------- FUNCIONALIDAD MERGEAR VIDEO ----------------
            SegmentMerger.MergeSegments(
                                        inputFilePatern: "Resources\\*.ts",
                                        outputFilePath: "merged.ts",
                                        startTime: TimeSpan.FromSeconds(7.5),
                                        recordDuration: TimeSpan.FromSeconds(14),
                                        options.HWDevice,
                                        ignoreAudio: false,
                                        defaultFps: 24
                                        );
        }
    }
}
