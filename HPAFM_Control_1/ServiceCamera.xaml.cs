using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for ControlCamera.xaml
    /// </summary>
    public partial class ServiceCamera : Window
    {
        IntPtr displayHandle = IntPtr.Zero;
        InterfaceThorCamera camInterface;

        /// <summary>
        /// Initialize camera window
        /// </summary>
        /// <param name="itc">Camera interface to get live images</param>
        /// <param name="hph">HPBox interface to control camera illumination LED</param>
        /// <param name="tml">ThorMotor linear interface to adjust camera focus</param>
        public ServiceCamera(InterfaceThorCamera itc)
        {
            camInterface = itc;// this is already pre-initialized by main window
            //displayHandle = dh;//camera shown in main window

            InitializeComponent();

            if (camInterface.IsLive)
            {
                SlowCheck.IsEnabled = false;
                StartCam.Content = "Stop Cam";
            }

            camInterface.liveRenderMode = uc480.Defines.DisplayRenderMode.DownScale_1_2;

            System.Windows.Forms.PictureBox picturebox1 = new System.Windows.Forms.PictureBox();
            WFHost.Child = picturebox1;
            displayHandle = picturebox1.Handle;
                        
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Camera Window is open.");
        }

        private void StartCam_Click(object sender, RoutedEventArgs e)
        {
            if (!camInterface.IsLive)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Starting video capture.");
                camInterface.StartVideoCapture(displayHandle, SlowCheck.IsChecked == true);
                SlowCheck.IsEnabled = false;
                StartCam.Content = "Stop Cam";
            }
            else
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Stopping video capture.");
                camInterface.StopVideoCapture();
                SlowCheck.IsEnabled = true;
                StartCam.Content = "Start Cam";
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Camera Window is closing.");

            if (camInterface.IsLive)
                camInterface.StopVideoCapture();
            // the camera de-init will be done by the main window
        }
    }
}
