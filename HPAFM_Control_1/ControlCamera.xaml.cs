using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HPAFM_Control_1
{
    /// <summary>
    /// Interaction logic for CameraController.xaml
    /// </summary>
    public partial class ControlCamera : Page
    {
        IntPtr displayHandle = IntPtr.Zero;
        InterfaceThorCamera camInterface;
        InterfaceThorMotorLinear lmInterface;
        InterfaceHPHardware hpInterface;

        bool processScroll = false; //for setting focus using a slider control

        public ControlCamera(InterfaceThorCamera cam, InterfaceThorMotorLinear lmi, InterfaceHPHardware ihp)
        {
            camInterface = cam;
            lmInterface = lmi;
            hpInterface = ihp;

            InitializeComponent();

            System.Windows.Forms.PictureBox picturebox1 = new System.Windows.Forms.PictureBox();
            WFHost.Child = picturebox1;
            displayHandle = picturebox1.Handle;

            if (!lmInterface.IsHomed)
            {
                ScrollPosition.Visibility = Visibility.Collapsed;
                CamMotorHome.Visibility = Visibility.Visible;
            }
            else
            {
                ScrollPosition.Visibility = Visibility.Visible;
                CamMotorHome.Visibility = Visibility.Collapsed;
                processScroll = false;
                ScrollPosition.Value = ScrollPosition.Maximum - lmInterface.MotorPosition;//reverse direction for easier UI
                processScroll = true;
            }
        }

        private void StartCam_Click(object sender, RoutedEventArgs e)
        {
            if (!camInterface.IsLive)
            {
                StartCamera(SlowCheck.IsChecked == true);
            }
            else
            {
                StopCamera();
            }
        }

        public void StartCamera(bool slowLive)
        {
            SlowCheck.IsChecked = slowLive;
            if (!camInterface.IsLive)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Starting video capture.");
                camInterface.StartVideoCapture(displayHandle, true);
                SlowCheck.IsEnabled = false;
                StartCam.Content = "Stop Cam";
            }
        }

        public void StopCamera()
        {
            if (camInterface.IsLive)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Stopping video capture.");
                camInterface.StopVideoCapture();
                SlowCheck.IsEnabled = true;
                StartCam.Content = "Start Cam";
            }
        }

        private async void ScrollPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!processScroll || !lmInterface.IsHomed)
                return; //to avoid recursive loop when programmatically setting scroll position

            ScrollPosition.IsEnabled = false;
            double v = ScrollPosition.Maximum - ScrollPosition.Value;//reverse direction for easier UI
            await Task.Run(() => lmInterface.MoveMotorAbs(v));
            ScrollPosition.IsEnabled = true;
        }

        private void ScrollPosition_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            processScroll = false; //don't continuously update position while the user drags scroll bar
        }

        private async void ScrollPosition_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (!lmInterface.IsHomed)
                return;

            //now that drag is completed, process the new position
            ScrollPosition.IsEnabled = false;
            double v = ScrollPosition.Maximum - ScrollPosition.Value;//reverse direction for easier UI
            await Task.Run(() => lmInterface.MoveMotorAbs(v));
            ScrollPosition.IsEnabled = true;

            processScroll = true;
        }

        private async void CamMotorHome_Click(object sender, RoutedEventArgs e)
        {
            if (!lmInterface.IsHomed)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Thorlabs Linear Motor homing started");
                MessageBoxResult hy = MessageBox.Show("Thorlabs Linear Motor (camera focus) has not been homed. Perform homing now? This will move camera forward towards the chamber.", "Homing", MessageBoxButton.OKCancel);
                if (hy == MessageBoxResult.OK)
                {
                    CamMotorHome.IsEnabled = false;
                    try
                    {
                        await Task.Run(() => lmInterface.HomeMotor());
                    }
                    catch (Exception x)
                    {
                        HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "Unable to home thorlabs linear motor: " + x.Message, true);
                        return;
                    }
                }
                else
                {
                    HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Operator cancelled thorlabs linear motor homing.");
                    return;
                }
            }

            ScrollPosition.Visibility = Visibility.Visible;
            CamMotorHome.Visibility = Visibility.Collapsed;
        }

        private void IllumCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            hpInterface.SetIllumination(IllumCheckbox.IsChecked == true);
        }

        public void SetIllumination(bool on)
        {
            IllumCheckbox.IsChecked = on;
        }
    }
}
