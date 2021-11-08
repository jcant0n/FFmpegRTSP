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
            var url = "rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
            ////var url = "rtsp://Foscam06:Foscam.06@192.168.1.10:88/videoMain";
            int segmentsToRecordCount = 8;
            int framesPerSegment = 500;
            var hWDevice = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

            // ---------------- FUNCIONALIDAD SEGMENTAR VIDEO ----------------
            RTSPStreamSegmentRecorder.RecordSegmentedRtspStream(url: url, 
                                                                usebitmapFiles: false, 
                                                                segmentsToRecordCount, 
                                                                framesPerSegment, 
                                                                hWDevice);

            // ---------------- FUNCIONALIDAD MERGEAR VIDEO ----------------
            SegmentMerger.MergeSegments(inputFilePatern: "segment*.mp4", 
                                        outputFilePath: "merged.mp4",
                                        startTime: TimeSpan.FromSeconds(30),
                                        recordDuration: TimeSpan.FromSeconds(30), 
                                        hWDevice);
        }

    }
}
