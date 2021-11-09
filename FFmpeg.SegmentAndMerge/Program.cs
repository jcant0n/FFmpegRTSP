using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FFmpeg.AutoGen.Example
{
    public static class Program
    {
        private static void Main(string[] args)
        {
            RTSPStreamSegmentRecorder.Initialize();

            // decode all frames from url, please not it might local resorce, e.g. string url = "../../sample_mpeg4.mp4";
            //var url = "http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4"; // be advised this file holds 1440 frames
            //var url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
            var url = "rtsp://192.168.1.233:554/11";

            var options = new RTSPStreamSegmentRecorder.SegmentRecordOptions();

            // ---------------- FUNCIONALIDAD SEGMENTAR VIDEO ----------------
            //RTSPStreamSegmentRecorder.RecordSegmentedRtspStream(url: url, options);

            // ---------------- FUNCIONALIDAD MERGEAR VIDEO ----------------
            SegmentMerger.MergeSegments(
                                        //inputFilePatern: "segment*.mp4",
                                        inputFilePatern: "Resources\\*.ts",
                                        outputFilePath: "merged.ts",
                                        startTime: TimeSpan.FromSeconds(0),
                                        recordDuration: TimeSpan.FromSeconds(10),
                                        options.HWDevice,
                                        ignoreAudio: true);
        }
    }
}
