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
    /// Interaction logic for ControlMotion.xaml
    /// </summary>
    public partial class ServiceMotion : Window
    {
        InterfacePIMotors piInterface;
        InterfaceThorMotorInertial tmInterface;
        InterfaceThorMotorLinear tlInterface;
        double stepSizePI = 1;//[mm]

        const int StepSizeInertial = 250; //increment of 250 is about 1/50th of a turn
        //motors should be set up as 1,2 are horizontal,vertical on mirror 1, 3,4 are horizontal,vertical on mirror 2 (closest to camera)

        bool processScroll = false;

        public ServiceMotion(InterfacePIMotors ipm, InterfaceThorMotorInertial itm, InterfaceThorMotorLinear itl)
        {
            piInterface = ipm;// this is already pre-initialized by main window and is homed
            tmInterface = itm;
            tlInterface = itl;

            InitializeComponent();

            /*ProbeAxisText.Text = piInterface.GetMotorPosition(1).ToString("00.000000");
            SampleAxisText.Text = piInterface.GetMotorPosition(2).ToString("00.000000");

            processScroll = false;
            ScrollPosition.Value = (double)tlInterface.MotorPosition;
            processScroll = true;*/

            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Motion Control Window is open.");
        }

        private void PIMove_Click(object sender, RoutedEventArgs e)
        {//move axis 1 in negative direction
            Button b = (Button)sender;
            string t = (string)b.Tag;

            try
            {
                switch (t[0])
                {
                    case '1':
                        if (t[1] == '+')
                        {
                            piInterface.MoveMotorInc(1, stepSizePI, true);
                        }
                        else
                        {
                            piInterface.MoveMotorInc(1, -stepSizePI, true);
                        }
                        ProbeAxisText.Text = piInterface.GetMotorPosition(1).ToString("00.000000");
                        break;
                    case '2':
                        if (t[1] == '+')
                        {
                            piInterface.MoveMotorInc(2, stepSizePI, true);
                        }
                        else
                        {
                            piInterface.MoveMotorInc(2, -stepSizePI, true);
                        }
                        SampleAxisText.Text = piInterface.GetMotorPosition(2).ToString("00.000000");
                        break;
                }
            }catch(Exception x)
            {
                HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Warning, "PIMove_Click: error " + x.Message, true);
            }
        }

        private void StepSize_Checked(object sender, RoutedEventArgs e)
        {// update step size with checkbox content
            RadioButton rb = (RadioButton)sender;
            if (rb.IsChecked == true)
            {
                stepSizePI = double.Parse((string)rb.Content);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            HPAFMLogger.LogMessage(HPAFMLogger.LogLevel.Info, "Motion Control Window is closing.");

            //interface de-init will be done by main window
        }

        private async void MotionButton_Click(object sender, RoutedEventArgs e)
        {//blocking function
            Button s = (Button)sender;
            string d = (string)s.Tag;
            if (d[0] == 'R')
            {//rotate
                switch (d[1])
                {
                    case 'U':
                        //motor 4 -
                        tmInterface.MoveMotorInc(4, -StepSizeInertial);
                        break;
                    case 'D':
                        //motor 4 +
                        tmInterface.MoveMotorInc(4, StepSizeInertial);
                        break;
                    case 'L':
                        //motor 3 +
                        tmInterface.MoveMotorInc(3, StepSizeInertial);
                        break;
                    case 'R':
                        //motor 3 -
                        tmInterface.MoveMotorInc(3, -StepSizeInertial);
                        break;
                }
            }
            else
            {//shift
                switch (d[1])
                {
                    case 'U':
                        //motor 2 -
                        tmInterface.MoveMotorInc(2, -StepSizeInertial);
                        //motor 4 +
                        tmInterface.MoveMotorInc(4, StepSizeInertial);
                        break;
                    case 'D':
                        //motor 2 +
                        tmInterface.MoveMotorInc(2, StepSizeInertial);
                        //motor 4 -
                        tmInterface.MoveMotorInc(4, -StepSizeInertial);
                        break;
                    case 'R':
                        //motor 1 +
                        tmInterface.MoveMotorInc(1, StepSizeInertial);
                        //motor 3 +
                        tmInterface.MoveMotorInc(3, StepSizeInertial);
                        break;
                    case 'L':
                        //motor 1 -
                        tmInterface.MoveMotorInc(1, -StepSizeInertial);
                        //motor 3 -
                        tmInterface.MoveMotorInc(3, -StepSizeInertial);
                        break;
                }
            }
        }

        private void ScrollPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!processScroll)
                return; //to avoid recursive loop when programmatically setting scroll position

            tlInterface.MoveMotorAbs(ScrollPosition.Value);
        }

        private void ScrollPosition_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            processScroll = false; //don't continuously update position while the user drags scroll bar
        }

        private void ScrollPosition_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            //now that drag is completed, process the new position
            tlInterface.MoveMotorAbs(ScrollPosition.Value);

            processScroll = true;
        }
    }
}
