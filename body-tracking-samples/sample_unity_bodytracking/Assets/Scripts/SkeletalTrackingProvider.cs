using Microsoft.Azure.Kinect.BodyTracking;
using Microsoft.Azure.Kinect.Sensor;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

public class SkeletalTrackingProvider : BackgroundDataProvider
{
    bool readFirstFrame = false;
    //時間間隔を表す構造体
    TimeSpan initialTimestamp;

    //関数にコロン = 初期化
    public SkeletalTrackingProvider(int id) : base(id)
    {
        Debug.Log("in the skeleton provider constructor");
    }

    System.Runtime.Serialization.Formatters.Binary.BinaryFormatter binaryFormatter { get; set; } =
        new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

    public Stream RawDataLoggingFile = null;

    protected override void RunBackgroundThreadAsync(int id, CancellationToken token)
    {
        try
        {
            UnityEngine.Debug.Log("Starting body tracker background thread.");

            //バッファ割り当て
            BackgroundData currentFrameData = new BackgroundData();
            //デバイスを開く
            //using Open出来なかったときフックする
            //非同期で値が帰ってくる
            using (Device device = Device.Open(id))
            {
                //カメラの設定
                device.StartCameras(new DeviceConfiguration()
                {
                    CameraFPS = FPS.FPS30,
                    ColorResolution = ColorResolution.Off,
                    DepthMode = DepthMode.NFOV_Unbinned,
                    WiredSyncMode = WiredSyncMode.Standalone,
                });

                UnityEngine.Debug.Log("Open K4A device successful. id " + id + "sn:" + device.SerialNum);

                var deviceCalibration = device.GetCalibration();

                using (Tracker tracker = Tracker.Create(deviceCalibration,
                    new TrackerConfiguration() {  //生成するときに初期値を入れておきたい時に使う
                    ProcessingMode = TrackerProcessingMode.Cuda,
                    SensorOrientation = SensorOrientation.Default
                    }))
                {
                    UnityEngine.Debug.Log("Body tracker created.");
                    while (!token.IsCancellationRequested)
                    {
                        using (Capture sensorCapture = device.GetCapture())
                        {
                            //センサーから最新のフレームをキューに入れます。
                            tracker.EnqueueCapture(sensorCapture);
                        }

                        //最新のトラッカーフレームを入手してみてください。
                        using (Frame frame = tracker.PopResult(TimeSpan.Zero, throwOnTimeout: false))
                        {
                            if (frame == null)
                            {
                                UnityEngine.Debug.Log("Pop result from tracker timeout!");
                            }
                            else
                            {
                                IsRunning = true;
                                //現在のフレームのボディの数を取得します。
                                currentFrameData.NumOfBodies = frame.NumberOfBodies;

                                //ボディをコピーします。
                                for (uint i = 0; i < currentFrameData.NumOfBodies; i++)
                                {
                                    currentFrameData.Bodies[i].CopyFromBodyTrackingSdk(frame.GetBody(i), deviceCalibration);
                                }

                                //深度画像を保存します
                                Capture bodyFrameCapture = frame.Capture;
                                Image depthImage = bodyFrameCapture.Depth;

                                if (!readFirstFrame)
                                {
                                    readFirstFrame = true;
                                    initialTimestamp = depthImage.DeviceTimestamp;
                                }
                                currentFrameData.TimestampInMs = (float)(depthImage.DeviceTimestamp - initialTimestamp).TotalMilliseconds;
                                currentFrameData.DepthImageWidth = depthImage.WidthPixels;
                                currentFrameData.DepthImageHeight = depthImage.HeightPixels;

                                //SDKから画像データを読み取ります
                                var depthFrame = MemoryMarshal.Cast<byte, ushort>(depthImage.Memory.Span);

                                //データを再パックし、画像データを保存します
                                int byteCounter = 0;

                                currentFrameData.DepthImageSize = currentFrameData.DepthImageWidth * currentFrameData.DepthImageHeight * 3;

                                for (int it = currentFrameData.DepthImageWidth * currentFrameData.DepthImageHeight - 1; it > 0; it--)
                                {
                                    byte b = (byte)(depthFrame[it] / (ConfigLoader.Instance.Configs.SkeletalTracking.MaximumDisplayedDepthInMillimeters) * 255);

                                    currentFrameData.DepthImage[byteCounter++] = b;
                                    currentFrameData.DepthImage[byteCounter++] = b;
                                    currentFrameData.DepthImage[byteCounter++] = b;
                                }

                                if (RawDataLoggingFile != null && RawDataLoggingFile.CanWrite)
                                {
                                    binaryFormatter.Serialize(RawDataLoggingFile, currentFrameData);
                                }

                                //UIスレッドで読み取られているデータ変数を更新します。
                                SetCurrentFrameData(ref currentFrameData);
                            }
                        }
                    }
                    Debug.Log("dispose of tracker now!!!!!");
                    tracker.Dispose();
                }
                device.Dispose();
            }
            if (RawDataLoggingFile != null)
            {
                RawDataLoggingFile.Close();
            }
        }
        catch (Exception e)
        {
            Debug.Log($"catching exception for background thread {e.Message}");
            token.ThrowIfCancellationRequested();
        }
    }
}