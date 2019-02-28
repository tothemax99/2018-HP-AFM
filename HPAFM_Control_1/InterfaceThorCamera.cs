using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPAFM_Control_1
{
    public class InterfaceThorCamera
    {
        //Copied from Thorlabs Included example: C:\Program Files\Thorlabs\Scientific Imaging\DCx Camera Support\Develop\Source\uc480_DotNet_C#_SimpleLive_2

        //Properties.Settings.Default.SlowLiveTimerInterval = 0.5; //slow live timer interval in seconds - how often to get a new frame
        private uc480.Camera Camera;
        private bool cameraFastLive = false, cameraSlowLive = false;
        IntPtr liveDisplayHandle = IntPtr.Zero;

        public uc480.Defines.DisplayRenderMode liveRenderMode = uc480.Defines.DisplayRenderMode.DownScale_1_2 | uc480.Defines.DisplayRenderMode.Rotate_180;
        public bool IsLive { get { return cameraFastLive || cameraSlowLive; } }

        private System.Windows.Threading.DispatcherTimer slowLiveTimer;

        public void InitializeCamera()
        {
            if (Camera != null)
                throw new ApplicationException("Thor Camera cannot be initialized as it is already initialized.");

            Camera = new uc480.Camera();//Use only the empty constructor, the one with cameraID has a bug

            uc480.Defines.Status statusRet = 0;

            // Open Camera
            statusRet = Camera.Init();//You can specify a particular cameraId here if you want to open a specific camera

            if (statusRet != uc480.Defines.Status.SUCCESS)
            {
                //MessageBox.Show("Camera initializing failed");
                throw new ApplicationException("Thor Camera initializing failed, statusRet=" + statusRet.ToString());
            }

            // Allocate Memory
            statusRet = Camera.Memory.Allocate(out Int32 s32MemID, true);
            if (statusRet != uc480.Defines.Status.SUCCESS)
            {
                //MessageBox.Show("Allocate Memory failed");
                throw new ApplicationException("Thor Camera allocate memory failed, statusRet=" + statusRet.ToString());
            }

            slowLiveTimer = new System.Windows.Threading.DispatcherTimer();
            slowLiveTimer.Interval = TimeSpan.FromSeconds(Properties.Settings.Default.SlowLiveTimerInterval);
            slowLiveTimer.Tick += SlowLiveTimer_Tick;

            // Connect frame capture/display Event which handles drawing when camera has captured an image
            Camera.EventFrame += onFrameEvent;
        }

        private void SlowLiveTimer_Tick(object sender, EventArgs e)
        {
            getSingleImage();
        }

        public void Exit()
        {
            if (Camera == null)
                return;

            if (IsLive)
                StopVideoCapture();

            Camera.Exit();

            Camera = null;
        }

        private void onFrameEvent(object sender, EventArgs e)
        {
            Camera.Memory.GetActive(out Int32 s32MemID);

            Camera.Display.Render(s32MemID, liveDisplayHandle, liveRenderMode);
        }

        public void SetAutoParams(bool autoGain, bool autoWhiteBal)
        {
            if (Camera.AutoFeatures.Software.Gain.Supported)
                Camera.AutoFeatures.Software.Gain.SetEnable(autoGain);
            if (Camera.AutoFeatures.Software.WhiteBalance.Supported)
                Camera.AutoFeatures.Software.WhiteBalance.SetEnable(autoWhiteBal);
        }

        public void StartVideoCapture(IntPtr displayHandle, bool slowLive)
        {
            if (IsLive)
                throw new ApplicationException("StartVideoCapture: camera is already live.");

            liveDisplayHandle = displayHandle;

            if (slowLive)
            {
                slowLiveTimer.Start();
                cameraSlowLive = true;
            }
            else
            {
                uc480.Defines.Status statusRet = Camera.Acquisition.Capture();
                if (statusRet != uc480.Defines.Status.SUCCESS)
                    throw new ApplicationException("StartVideoCapture: camera starting failed, statusRet=" + statusRet.ToString());
                cameraFastLive = true;
            }
        }

        public void StopVideoCapture()
        {
            if (!IsLive)
                throw new ApplicationException("StopVideoCapture: camera is already stopped.");

            if (cameraSlowLive)
                slowLiveTimer.Stop();

            cameraSlowLive = false;

            if (cameraFastLive)
            {
                uc480.Defines.Status statusRet = Camera.Acquisition.Stop();
                if (statusRet != uc480.Defines.Status.SUCCESS)
                    throw new ApplicationException("StopVideoCapture: camera stop failed, statusRet=" + statusRet.ToString());
            }

            cameraFastLive = false;
        }

        public void GetSingleImage(IntPtr displayHandle)
        {
            if (IsLive)
                throw new ApplicationException("GetSingleImage: cannot capture image while camera is live.");

            liveDisplayHandle = displayHandle;

            uc480.Defines.Status statusRet = Camera.Acquisition.Freeze();
            if (statusRet != uc480.Defines.Status.SUCCESS)
                throw new ApplicationException("GetSingleImage: acquisition failed, statusRet=" + statusRet.ToString());
        }

        void getSingleImage()
        { //for calling by slowLive timer only
            if (!cameraSlowLive)
                return;

            Camera.Acquisition.Freeze();
        }
    }
}
